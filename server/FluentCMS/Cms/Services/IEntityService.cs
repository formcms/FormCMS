using System.Text.Json;
using Microsoft.Extensions.Primitives;
using FluentCMS.Utils.QueryBuilder;
using Attribute = FluentCMS.Utils.QueryBuilder.Attribute;

namespace FluentCMS.Cms.Services;

public interface IEntityService
{
    Task<ListResult?> List(string name,Pagination pagination, Dictionary<string, StringValues> args, CancellationToken token);
    Task<Record> Insert(string name, JsonElement item, CancellationToken token = default);
    Task<Record> Update(string name, JsonElement item, CancellationToken token);
    Task<Record> Delete(string name, JsonElement item, CancellationToken token);
    Task<Record> One(string entityName, string strId, CancellationToken token);
    Task<Record> OneByAttributes(string entityName, string strId, string[]attributes, CancellationToken token =default);
    
    Task<ListResult> CrosstableList(string name, string id, string attr, bool exclude, Dictionary<string,StringValues> args, Pagination pagination, CancellationToken token);
    Task<int> CrosstableAdd(string name, string id, string attr, JsonElement[] eles, CancellationToken token);
    Task<int> CrosstableDelete(string name, string id, string attr, JsonElement[] eles, CancellationToken token);
}