using System.Collections.Immutable;
using FluentCMS.Cms.Models;
using FluentCMS.Services;
using FluentCMS.Utils.Cache;
using FluentCMS.Utils.DataDefinitionExecutor;
using FluentCMS.Utils.QueryBuilder;
using FluentResults;
using Attribute = FluentCMS.Utils.QueryBuilder.Attribute;

namespace FluentCMS.Cms.Services;
using static InvalidParamExceptionFactory;

public sealed class EntitySchemaService(
    ISchemaService schemaSvc,
    IDefinitionExecutor executor,
    KeyValueCache<ImmutableArray<Entity>> entityCache
) : IEntitySchemaService
{
    public ValueTask<ImmutableArray<Entity>> GetOrCreate(CancellationToken ct = default)
    {
        return entityCache.GetOrSet("", async token =>
        {
            var schemas = await schemaSvc.All(SchemaType.Entity, null, token);
            var entities = schemas
                .Where(x => x.Settings.Entity is not null)
                .Select(x => x.Settings.Entity!);
            return [..entities];
        }, ct);
    }

    public bool ResolveVal(Attribute attr, string v, out ValidValue result)
    {
        result = executor.TryParseDataType(v, attr.DataType, out var val) switch
        {
            true => new ValidValue(val!.S, val.I, val.D),
            _ => new ValidValue()
        };
        return !result.IsEmpty();
    }

    public async Task<Result<AttributeVector>> ResolveVector(LoadedEntity entity, string fieldName)
    {
        var fields = fieldName.Split(".");
        var prefix = string.Join(AttributeVectorConstants.Separator, fields[..^1]);
        var attributes = new List<LoadedAttribute>();
        LoadedAttribute? attr = null;
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            attr = entity.Attributes.FindOneAttr(field);
            if (attr is null)
            {
                return Result.Fail($"Fail to attribute vector: can not find {field} in {entity.Name} ");
            }

            if (i == fields.Length - 1) break;

            var res = await LoadOneCompoundAttribute(entity, attr,[], default);
            if (res.IsFailed)
            {
                return Result.Fail(res.Errors);
            }

            attr = res.Value;
            switch (attr.Type)
            {
                case DisplayType.Crosstable:
                    entity = attr.Crosstable!.TargetEntity;
                    break;
                case DisplayType.Lookup:
                    entity = attr.Lookup!;
                    break;
                default:
                    return Result.Fail($"Can not resolve {fieldName}, {attr.Field} is not a composite type");
            }

            attributes.Add(attr);
        }

        return new AttributeVector(fieldName, prefix, [..attributes], attr!);

    }

    public async Task<Result<LoadedEntity>> GetLoadedEntity(string name, CancellationToken token = default)
    {
        var (_, isFailed, entity, errors) = await GetEntity(name, token);
        if (isFailed)
        {
            return Result.Fail(errors);
        }

        var ret = await LoadCompoundAttributes(entity.ToLoadedEntity(), [], token);
        return ret;
    }

    private async Task<Result<Entity>> GetEntity(string name, CancellationToken token = default)
    {
        var item = await schemaSvc.GetByNameDefault(name, SchemaType.Entity, token);
        if (item is null)
        {
            return Result.Fail($"can not find entity {name} ");
        }

        var entity = item.Settings.Entity;
        if (entity is null)
        {
            return Result.Fail($"entity {name} is invalid");
        }

        return entity;
    }

    public async Task<Entity?> GetTableDefine(string name, CancellationToken token)
    {
        var cols = await executor.GetColumnDefinitions(name, token);
        return new Entity
        (
            Attributes: [..cols.Select(AttributeHelper.ToAttribute)]
        );
    }


    public async Task<Schema> SaveTableDefine(Schema dto, CancellationToken ct = default)
    {
        Ok(await schemaSvc.NameNotTakenByOther(dto, ct));
        var entity = NotNull(dto.Settings.Entity).ValOrThrow("invalid payload").WithDefaultAttr();
        var cols = await executor.GetColumnDefinitions(entity.TableName, ct);
        Ok(EnsureTableNotExist());
        await VerifyEntity(entity, ct);
        await SaveSchema(); //need  to save first because it will call trigger
        await CreateCrosstables();
        await SaveMainTable();
        await schemaSvc.EnsureEntityInTopMenuBar(entity, ct);
        await entityCache.Remove("",ct);
        return dto;

        async Task SaveSchema()
        {
            dto = dto with { Settings = new Settings(entity) };
            dto = await schemaSvc.SaveWithAction(dto, ct);
            entity = dto.Settings.Entity!;
        }

        async Task SaveMainTable()
        {
            if (cols.Length > 0) //if table exists, alter table add columns
            {
                var columnDefinitions = entity.AddedColumnDefinitions(cols);
                if (columnDefinitions.Length > 0)
                {
                    await executor.AlterTableAddColumns(entity.TableName, columnDefinitions,
                        ct);
                }
            }
            else
            {
                await executor.CreateTable(entity.TableName, entity.Definitions().EnsureDeleted(),
                    ct);
            }
        }

        async Task CreateCrosstables()
        {
            foreach (var attribute in entity.Attributes.GetAttrByType(DisplayType.Crosstable))
            {
                await CreateCrosstable(entity.ToLoadedEntity(),
                    attribute.ToLoaded(entity.TableName), ct);
            }
        }

        Result EnsureTableNotExist()
        {
            var creatingNewEntity = dto.Id == 0;
            var tableExists = cols.Length > 0;
            return creatingNewEntity && tableExists
                ? Result.Fail($"Fail to add new entity, the table {entity.TableName} already exists")
                : Result.Ok();
        }
    }

    private async Task<Result<LoadedAttribute>> LoadLookup(LoadedAttribute attr, CancellationToken ct)
    {
        if (attr.Lookup is not null)
        {
            return attr;
        }

        if (!attr.GetLookupTarget(out var lookupName))
        {
            return Result.Fail($"Lookup Option was not set for attribute `{attr.Field}`");
        }

        var (_, isFailed, value, _) = await GetEntity(lookupName, ct);
        if (isFailed)
        {
            return Result.Fail(
                $"not find entity by name {lookupName} for lookup {attr.Field}");
        }

        return attr with { Lookup = value.ToLoadedEntity() };
    }

    private async Task<Result<LoadedAttribute>> LoadCrosstable(
        LoadedEntity entity, 
        LoadedAttribute attr,
        HashSet<string> visitedCrosstable,
        CancellationToken token)
    {
        if (attr.Crosstable is not null)
        {
            return attr;
        }

        if (!attr.GetCrosstableTarget(out var targetName))
        {
            return Result.Fail($"Crosstable Option was not set for attribute `{entity.Name}.{attr.Field}`");
        }
        
        var crosstableTableName = CrosstableHelper.GetCrosstableTableName(entity.Name, targetName);
        if (!visitedCrosstable.Add(crosstableTableName))
        {
            return attr;
        }

        var (_, _, target, getErr) = await GetEntity(targetName, token);
        if (getErr is not null)
        {
            return Result.Fail($"not find entity by name {targetName}, err = {getErr}");
        }

        var (_, _, loadedTarget, loadErr) = await LoadCompoundAttributes(target.ToLoadedEntity(), visitedCrosstable, token);
        if (loadErr is not null)
        {
            return Result.Fail(loadErr);
        }

        return attr with { Crosstable = CrosstableHelper.Crosstable(entity, loadedTarget!, attr) };
    }

    public async Task<Result<LoadedAttribute>> LoadOneCompoundAttribute(
        LoadedEntity entity, 
        LoadedAttribute attr,
        HashSet<string> visitedCrosstable,
        CancellationToken token)
    {
        return attr.Type switch
        {
            DisplayType.Crosstable => await LoadCrosstable(entity, attr,visitedCrosstable, token),
            DisplayType.Lookup => await LoadLookup(attr, token),
            _ => attr
        };
    }

    public async Task Delete(Schema schema, CancellationToken ct)
    {
        await schemaSvc.Delete(schema.Id, ct);
        await entityCache.Remove("",ct);
    }

    public async Task<Schema> Save(Schema schema, CancellationToken ct)
    {
        var ret = await schemaSvc.SaveWithAction(schema, ct);
        await  entityCache.Remove("",ct);
        return ret;
    }

    private async Task<Result<LoadedEntity>> LoadCompoundAttributes(LoadedEntity entity, HashSet<string> visitedCrosstable, CancellationToken ct)
    {
        var lst = new List<LoadedAttribute>();

        foreach (var attribute in entity.Attributes)
        {
            switch (attribute)
            {
                case { Type: DisplayType.Lookup  or DisplayType.Crosstable }:
                    var (_, _, value, errors) = await LoadOneCompoundAttribute(entity, attribute, visitedCrosstable,ct);
                    if (errors is not null)
                    {
                        return Result.Fail(errors);
                    }

                    lst.Add(value);
                    break;

                default:
                    lst.Add(attribute);
                    break;
            }
        }

        return entity with { Attributes = [..lst] };
    }

    private async Task CreateCrosstable(LoadedEntity entity, LoadedAttribute attr, CancellationToken ct)
    {
        if (!attr.GetCrosstableTarget(out var crosstableName))
        {
            throw new Exception($"Crosstable Option was not set for attribute `{entity.Name}.{attr.Field}`");
        }

        var targetEntity = Ok(await GetLoadedEntity(crosstableName, ct));
        var crossTable = CrosstableHelper.Crosstable(entity, targetEntity, attr);
        var columns =
            await executor.GetColumnDefinitions(crossTable.CrossEntity.TableName, ct);
        if (columns.Length == 0)
        {
            await executor.CreateTable(crossTable.CrossEntity.TableName, crossTable.GetColumnDefinitions(),
                ct);
        }
    }

    private async Task CheckLookup(Attribute attr, CancellationToken ct)
    {
        True(attr.DataType == DataType.Int).ThrowNotTrue("lookup datatype should be int");
        if (!attr.GetLookupTarget(out var lookupName))
        {
            throw new InvalidParamException($"Lookup Option was not set for attribute `{attr.Field}`");
        }

        NotNull(await GetEntity(lookupName, ct))
            .ValOrThrow($"not find entity by name {lookupName}");
    }

    private async Task VerifyEntity(Entity entity, CancellationToken ct)
    {
        NotNull(entity.Attributes.FindOneAttr(entity.TitleAttribute))
            .ValOrThrow($"`{entity.TitleAttribute}` was not in attributes list");
        foreach (var attribute in entity.Attributes.GetAttrByType(DisplayType.Lookup))
        {
            await CheckLookup(attribute, ct);
        }
    }

    public async Task<Schema> AddOrUpdateByName(Entity entity, CancellationToken ct)
    {
        var find = await schemaSvc.GetByNameDefault(entity.Name, SchemaType.Entity, ct);
        var schema = new Schema
        (
            Id: find?.Id ?? 0,
            Name: entity.Name,
            Type: SchemaType.Entity,
            Settings: new Settings
            (
                Entity: entity
            ),
            CreatedBy: ""
        );
        return await SaveTableDefine(schema, ct);
    }
}