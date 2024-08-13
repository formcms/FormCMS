using System.Text.Json;
using Microsoft.Extensions.Primitives;
using FluentCMS.Utils.QueryBuilder;
using Attribute = FluentCMS.Utils.QueryBuilder.Attribute;

namespace FluentCMS.Services;

public interface IEntityService
{
    Task<object> List(string entityName,Pagination? pagination, Dictionary<string, StringValues> qs, CancellationToken cancellationToken);
    Task<object> List(string entityName, Filters? filters, Sorts? sorts, Pagination? pagination, CancellationToken cancellationToken);
    Task<object> Insert(string entityName, JsonElement item, CancellationToken cancellationToken = default);
    Task<object> Insert(string entityName, Record item, CancellationToken cancellationToken = default);
    Task<object> Update(string entityName, JsonElement item, CancellationToken cancellationToken);
    Task<object> Update(string entityName, Record item, CancellationToken cancellationToken);
    Task<object> Delete(string entityName, JsonElement item, CancellationToken cancellationToken);
    Task<object> Delete(string entityName, Record item, CancellationToken cancellationToken);
    Task<object> One(string entityName, string strId, CancellationToken cancellationToken);
    Task<ListResult> CrosstableList(string entityName, string strId, string field, bool exclude, CancellationToken cancellationToken);
    Task<int> CrosstableSave(string entityName, string strId, string field, JsonElement[] items, CancellationToken cancellationToken);
    Task<int> CrosstableDelete(string entityName, string strId, string attributeName, JsonElement[] elements, CancellationToken cancellationToken);
    Task AttachLookup(Attribute lookupAttribute, Record[] items, CancellationToken cancellationToken, Func<Entity, Attribute[]> getFields);
    Task AttachCrosstable(Attribute crossTableAttribute, Record[] items, CancellationToken cancellationToken,Func<Entity, Attribute[]> getFields);
}