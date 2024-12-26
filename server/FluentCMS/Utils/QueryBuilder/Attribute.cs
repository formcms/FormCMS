using System.Collections.Immutable;
using System.Globalization;

namespace FluentCMS.Utils.QueryBuilder;

public record Attribute(
    string Field,
    string Header = "",
    string DataType = DataType.String,
    string DisplayType = DisplayType.Text,
    bool InList = true,
    bool InDetail = true,
    bool IsDefault = false,
    string Options = "",
    string Validation = "",
    string ValidationMessage = ""
);

public record LoadedAttribute(
    string TableName,
    string Field,

    string Header = "",
    string DataType = DataType.String,
    string DisplayType = DisplayType.Text,

    bool InList = true,
    bool InDetail = true,
    bool IsDefault = false,

    string Options = "", 
    string Validation = "",
    string ValidationMessage = "",
    
    Junction? Junction = null,
    LoadedEntity? Lookup = null
) : Attribute(
    Field: Field,
    Header: Header,
    DisplayType: DisplayType,
    DataType: DataType,
    InList: InList,
    InDetail: InDetail,
    IsDefault:IsDefault,
    Validation:Validation,
    ValidationMessage:ValidationMessage,
    Options: Options
);

public sealed record GraphAttribute(
    ImmutableArray<GraphAttribute> Selection,
    ImmutableArray<ValidSort> Sorts,
    ImmutableArray<ValidFilter> Filters,
    
    string Prefix,
    string TableName,
    string Field,

    string Header = "",
    string DataType = DataType.String,
    string DisplayType = DisplayType.Text,

    bool InList = true,
    bool InDetail = true,
    bool IsDefault = false,

    string Options = "", 
    string Validation = "",
    string ValidationMessage = "",
    
    LoadedEntity? Lookup = null,
        
    Junction? Junction = null,
    Pagination? Pagination = null
    
) : LoadedAttribute(
    TableName:TableName,
    Field:Field,

    Header :Header,
    DataType : DataType,
    DisplayType : DisplayType,

    InList : InList,
    InDetail : InDetail,
    IsDefault : IsDefault,

    Options :Options, 
    Validation : Validation,
    ValidationMessage : ValidationMessage,
    
    Junction:Junction,
    Lookup :Lookup
);

public static class AttributeHelper
{

    public static LoadedAttribute ToLoaded(this Attribute a, string tableName)
    {
        return new LoadedAttribute(
            TableName: tableName,
            Field: a.Field,
            Header: a.Header,
            DataType: a.DataType,
            DisplayType: a.DisplayType,
            InList: a.InList,
            InDetail: a.InDetail,
            IsDefault: a.IsDefault,
            Options: a.Options,
            Validation: a.Validation,
            ValidationMessage: a.ValidationMessage
        );
    }

    public static GraphAttribute ToGraph(this LoadedAttribute a)
    {
        return new GraphAttribute(
            Prefix: "",
            Selection: [],
            Filters: [],
            Sorts: [],
            Pagination:new Pagination(),
            Lookup: a.Lookup,
            Junction: a.Junction,
            TableName: a.TableName,
            Field: a.Field,
            Header: a.Header,
            DataType: a.DataType,
            DisplayType: a.DisplayType,
            InList: a.InList,
            InDetail: a.InDetail,
            IsDefault: a.IsDefault,
            Options: a.Options,
            Validation: a.Validation,
            ValidationMessage: a.ValidationMessage
        );
    }


    public static string AddTableModifier(this LoadedAttribute attribute, string tableAlias = "")
    {
        if (tableAlias == "")
        {
            tableAlias = attribute.TableName;
        }

        return $"{tableAlias}.{attribute.Field}";
    }

    public static string FullPathName(this Attribute a, string prefix)
    {
        return prefix == "" ? a.Field : prefix + "." + a.Field;
    }

    public static bool GetLookupTarget(this Attribute a, out string val)
    {
        val = a.Options;
        return !string.IsNullOrWhiteSpace(val);
    }
    public static bool GetSelectItems(this Attribute a, out string[] arr)
    {
        arr = a.Options.Split(',');
        return arr.Length > 0;
    }

    public static bool GetJunctionTarget(this Attribute a, out string val)
    {
       val = a.Options;
       return !string.IsNullOrWhiteSpace(val);
    }

    public static bool IsCompound(this Attribute a)
    {
        return a.DataType is DataType.Lookup or DataType.Junction;
    }
    
    public static Attribute ToAttribute(string name, string colType)
    {
        return new Attribute(
            Field: name,
            Header: SnakeToTitle(name),
            DataType: colType 
        );

        string SnakeToTitle(string snakeStr)
        {
            var components = snakeStr.Split('_');
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i].Length > 0)
                {
                    components[i] = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(components[i]);
                }
            }
            return string.Join(" ", components);
        }
    }


    public static ValidValue[] GetUniq<T>(this T a, IEnumerable<Record> records)
        where T : Attribute 
    {
        var ret = new List<ValidValue>();
        foreach (var record in records)
        {
            if (record.TryGetValue(a.Field, out var value) && value != null)
            {
                ret.Add(value.ToValidValue());
            }
        }

        return ret.ToArray();
    }

    public static T? FindOneAttr<T>(this IEnumerable<T>? arr, string name)
        where T : Attribute
    {
        return arr?.FirstOrDefault(x => x.Field == name);
    }

    public static T[] GetLocalAttrs<T>(this IEnumerable<T>? arr)
        where T : Attribute
    {
        return arr?.Where(x => x.DataType != DataType.Junction).ToArray() ?? [];
    }

    public static T[] GetLocalAttrs<T>(this IEnumerable<T>? arr, string primaryKey, InListOrDetail listOrDetail)
        where T : Attribute
    {
        return arr?.Where(x =>
            x.Field == primaryKey
            || x.DataType != DataType.Junction && (listOrDetail == InListOrDetail.InList ? x.InList : x.InDetail)
        ).ToArray() ?? [];
    }

    public static T[] GetLocalAttrs<T>(this IEnumerable<T>? arr, string[] attributes)
        where T : Attribute
    {
        return arr?.Where(x => x.DataType != DataType.Junction && attributes.Contains(x.Field)).ToArray() ?? [];
    }

    public static T[] GetAttrByType<T>(this IEnumerable<T>? arr, string t)
        where T : Attribute
    {
        return arr?.Where(x => x.DataType == t).ToArray() ?? [];
    }

    public static T[] GetAttrByType<T>(this IEnumerable<T>? arr, string type,
        InListOrDetail listOrDetail)
        where T : Attribute
    {
        return arr?.Where(x => x.DataType == type && (listOrDetail == InListOrDetail.InList ? x.InList : x.InDetail))
            .ToArray() ?? [];
    }

    public static GraphAttribute? RecursiveFind(this IEnumerable<GraphAttribute> attributes, string name)
    {
        var parts = name.Split('.');
        var attrs = attributes;
        foreach (var part in parts[..^1])
        {
            var find = attrs.FindOneAttr(part);
            if (find == null)
            {
                return null;
            }

            attrs = find.Selection;
        }

        return attrs.FindOneAttr(parts.Last());
    }
}
