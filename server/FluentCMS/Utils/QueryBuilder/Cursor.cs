using System.Collections.Immutable;
using System.Text.Json;
using FluentCMS.Utils.DictionaryExt;
using FluentResults;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Utils.QueryBuilder;

public sealed record Cursor(string? First = default, string? Last = default);

public sealed record ValidCursor(Cursor Cursor, ImmutableDictionary<string,object>? EdgeItem = default);

public static class CursorConstants
{
    public const string Cursor = "cursor";
    public const string HasPreviousPage = "hasPreviousPage";
    public const string HasNextPage = "hasNextPage";
}

public static class CursorHelper
{
    
    public static Result<object> Edge(this ValidCursor c, string fld) => c.EdgeItem![fld];
    
    public static bool IsEmpty(this Cursor c) => string.IsNullOrWhiteSpace(c.First) && string.IsNullOrWhiteSpace(c.Last);
    
    public static bool IsForward(this Cursor c) => !string.IsNullOrWhiteSpace(c.Last) || string.IsNullOrWhiteSpace(c.First);
    
    public static string GetCompareOperator(this Cursor c,string order)
    {
        return  c.IsForward() ? order == SortOrder.Asc ? ">" : "<":
            order == SortOrder.Asc ? "<" : ">";
    }

    public static Result<Cursor> GetNextCursor(this Cursor c, Record[] items, ImmutableArray<ValidSort>? sorts, bool hasMore)
    {
        if (sorts is null)
        {
            return Result.Fail("Can not generate next cursor, sort was not set");
        }

        if (items.Length == 0)
        {
            return Result.Fail("No result, can not generate cursor");
        }

        var (hasPrevious, hasNext) = (hasMore, c.First,c.Last) switch
        {
            (true, "", "") => (false, true), // home page, should not has previous
            (false, "", "") => (false, false), // home page
            (true, _, _) => (true, true), // no matter click next or previous, show both
            (false, _, "") => (false, true), // click preview, should have next
            (false, "", _) => (true, false), // click next, should nave previous
            _ => (false, false)
        };
        
        return new Cursor
        (
            First : hasPrevious? EncodeRecord(items.First(), sorts):"",
            Last : hasNext? EncodeRecord(items.Last(), sorts):""
        );
    }

    private static string EncodeRecord(Record item, IEnumerable<ValidSort> sorts)
    {
        var dict = new Dictionary<string, object>();
        foreach (var sort in sorts)
        {
            var (_, _, val, error) = item.GetValueByPath(sort.Vector.FullPathFiledName);
            if (error is null)
            {
                dict[sort.Vector.FullPathFiledName] = val;
            }
        }
        var cursor = JsonSerializer.Serialize(dict);
        return Base64UrlEncoder.Encode(cursor);
    }

    
    public static Result<ValidCursor> Resolve(this Cursor c,LoadedEntity entity)
    {
        if (c.IsEmpty()) return new ValidCursor(c, default);
        
        try
        {
            var recordStr = c.IsForward() ? c.Last : c.First;
            recordStr = Base64UrlEncoder.Decode(recordStr);
            var element = JsonSerializer.Deserialize<JsonElement>(recordStr);
            var parseRes = entity.Parse(element);
            return parseRes.IsFailed ? Result.Fail(parseRes.Errors) : Result.Ok(new ValidCursor(c,parseRes.Value.ToImmutableDictionary()));
        }
        catch (Exception e)
        {
            return Result.Fail(e.Message);
        }
    }
}