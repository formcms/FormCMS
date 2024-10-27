using System.Collections.Immutable;
using FluentCMS.Utils.DataDefinitionExecutor;
using FluentCMS.Utils.Qs;
using FluentResults;
using Microsoft.Extensions.Primitives;

namespace FluentCMS.Utils.QueryBuilder;

public sealed record Filter(string FieldName, string Operator, ImmutableArray<Constraint> Constraints, bool OmitFail);

public sealed record ValidFilter(
    string FullPath,
    ImmutableArray<LoadedAttribute> Attributes,
    string Operator,
    ImmutableArray<ValidConstraint> Constraints) 
    : AttributeVector(FullPath: FullPath,Attributes: Attributes);

public static class FilterHelper
{
    
    public static async Task<Result<ImmutableArray<ValidFilter>>> Resolve(
        this IEnumerable<Filter> filters,  
        LoadedEntity entity,
        Dictionary<string, StringValues>? querystringDictionary,
        ResolveAttributeDelegate resolveAttribute 
        )
    {
        var ret = new List<ValidFilter>();
        foreach (var filter in filters)
        {
            var attributeRes = await resolveAttribute(entity,filter.FieldName);
            if (attributeRes.IsFailed)
            {
                return Result.Fail(attributeRes.Errors);
            }
            var attributes = attributeRes.Value;
            var res = filter.Constraints.Resolve(entity,attributes.Last(), filter.OmitFail,querystringDictionary);
            if (res.IsFailed)
            {
                return Result.Fail(res.Errors);
            }

            if (res.Value.Length > 0)
            {
                ret.Add(new ValidFilter(filter.FieldName,[..attributes],filter.Operator, res.Value));
            }
        }

        return ret.ToImmutableArray();
    }
    
    public static async Task<Result<ImmutableArray<ValidFilter>>> Parse(LoadedEntity entity, QsDict qsDict, ResolveAttributeDelegate resolveAttribute)
    {
        var ret = new List<ValidFilter>();
        foreach (var pair in qsDict.Dict)
        {
            if (pair.Key == SortHelper.SortKey)
            {
                continue;
            }
            var result =await Parse(entity, pair.Key, pair.Value.ToArray(),resolveAttribute );
            if (result.IsFailed)
            {
                return Result.Fail(result.Errors);
            }
            ret.Add(result.Value);
        }

        return ret.ToImmutableArray();
    }

    private static async Task<Result<ValidFilter>> Parse(LoadedEntity entity, string field, Pair[] pairs, 
        ResolveAttributeDelegate resolveAttribute)
    {
        var res  = await resolveAttribute(entity, field);
        if (res.IsFailed)
        {
            return Result.Fail($"Fail to parse filter, not found {entity.Name}.{field}");
        }

        var attributes = res.Value;
        var op = pairs.FirstOrDefault(x => x.Key == "operator")?.Values.FirstOrDefault() ?? "and";
        return new ValidFilter(field, attributes, op,
            [
                ..from pair in pairs.Where(x => x.Key != "operator")
                    from pairValue in pair.Values
                    select new ValidConstraint(pair.Key, [attributes.Last().Cast(pairValue)])
            ]);
    }
}