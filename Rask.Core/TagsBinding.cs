using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Routing;
using F = Rask.Core.Components;

namespace Rask.Core;

public static partial class Tags
{
    public static F.Input Input<TProp>(
        Expression<Func<TProp>> Bind,
        string? Type = null,
        string? Name = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        string? Min = null,
        string? Max = null,
        string? Step = null,
        string? Pattern = null,
        int? Size = null,
        int? MaxLength = null,
        int? MinLength = null,
        string? Autocomplete = null,
        bool Autofocus = false,
        string? List = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = EditContextScope.Current;
        var fid = acc.Field;
        var resolvedType = Type ?? DefaultInputType(acc.PropertyType);
        var name = Name ?? acc.PropertyName;

        var current = acc.Getter();

        if (resolvedType == "checkbox")
        {
            var isChecked = current is bool b && b;
            return Input(
                "checkbox", name,
                Checked: isChecked,
                Required: Required, Disabled: Disabled, ReadOnly: ReadOnly,
                Min: Min, Max: Max, Step: Step, Pattern: Pattern,
                Size: Size, MaxLength: MaxLength, MinLength: MinLength,
                Autocomplete: Autocomplete, Autofocus: Autofocus, List: List,
                OnChange: BoolToggleHandler(acc, ctx, fid, isChecked),
                Id: Id, Class: Class, Style: Style, Data: Data);
        }

        var stringValue = FormatValue(current);
        var isImmediate = IsImmediateUpdateType(acc.PropertyType);

        return Input(
            resolvedType, name, stringValue, Placeholder,
            Required, Disabled, ReadOnly,
            Min: Min, Max: Max, Step: Step, Pattern: Pattern,
            Size: Size, MaxLength: MaxLength, MinLength: MinLength,
            Autocomplete: Autocomplete, Autofocus: Autofocus, List: List,
            OnInput: isImmediate ? StringSetHandler(acc, ctx, fid, false) : null,
            OnChange: TouchAndValidateHandler(acc, ctx, fid, !isImmediate),
            Id: Id, Class: Class, Style: Style, Data: Data);
    }

    public static F.Textarea Textarea<TProp>(
        Expression<Func<TProp>> Bind,
        string? Name = null,
        int? Rows = null,
        int? Cols = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        int? MaxLength = null,
        int? MinLength = null,
        string? Wrap = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = EditContextScope.Current;
        var fid = acc.Field;
        var name = Name ?? acc.PropertyName;
        var stringValue = FormatValue(acc.Getter());

        return Textarea(
            name, Rows, Cols, Placeholder,
            Required, Disabled, ReadOnly,
            MaxLength, MinLength, Wrap,
            Autofocus, Autocomplete,
            OnInput: StringSetHandler(acc, ctx, fid, false),
            OnChange: TouchAndValidateHandler(acc, ctx, fid, false),
            Id: Id, Class: Class, Style: Style, Data: Data,
            Children: [stringValue]);
    }

    public static F.Select Select<TProp>(
        Expression<Func<TProp>> Bind,
        string? Name = null,
        bool Required = false,
        bool Disabled = false,
        int? Size = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        IEnumerable<Child>? Children = null)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = EditContextScope.Current;
        var fid = acc.Field;
        var name = Name ?? acc.PropertyName;
        var current = FormatValue(acc.Getter());
        var preselected = MarkSelected(Children, current);

        return Select(
            name, Required: Required, Disabled: Disabled, Size: Size,
            Autofocus: Autofocus, Autocomplete: Autocomplete,
            OnChange: TouchAndValidateHandler(acc, ctx, fid, true),
            Id: Id, Class: Class, Style: Style, Data: Data,
            Children: preselected);
    }

    public static F.ValidationMessage ValidationMessage<TProp>(
        Expression<Func<TProp>> For,
        string? Class = null) => new(For, Class);

    public static F.ValidationSummary ValidationSummary(string? Class = null) => new(Class);

    private static string DefaultInputType(Type propType)
    {
        var t = Nullable.GetUnderlyingType(propType) ?? propType;
        if (t == typeof(bool))
        {
            return "checkbox";
        }

        if (t == typeof(int) || t == typeof(long) || t == typeof(short) ||
            t == typeof(double) || t == typeof(float) || t == typeof(decimal))
        {
            return "number";
        }

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
        {
            return "datetime-local";
        }

        if (t == typeof(DateOnly))
        {
            return "date";
        }

        if (t == typeof(TimeOnly) || t == typeof(TimeSpan))
        {
            return "time";
        }

        return "text";
    }

    private static bool IsImmediateUpdateType(Type propType) =>
        (Nullable.GetUnderlyingType(propType) ?? propType) == typeof(string);

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "";
        }

        return value switch
        {
            string s => s,
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    private static Action<string> StringSetHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool validateOnSet) =>
        raw =>
        {
            if (TrySetTyped(acc, raw))
            {
                ctx?.NotifyFieldChanged(fid);
                if (validateOnSet)
                {
                    ctx?.ValidateField(fid);
                }
            }
        };

    private static Action<string> TouchAndValidateHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool setOnChange) =>
        raw =>
        {
            if (setOnChange)
            {
                if (TrySetTyped(acc, raw))
                {
                    ctx?.NotifyFieldChanged(fid);
                }
            }

            ctx?.NotifyFieldTouched(fid);
            ctx?.ValidateField(fid);
        };

    private static Action<string> BoolToggleHandler(
        ExpressionAccessor.Accessor acc, EditContext? ctx, FieldIdentifier fid, bool wasChecked) =>
        _ =>
        {
            acc.Setter(!wasChecked);
            ctx?.NotifyFieldChanged(fid);
            ctx?.NotifyFieldTouched(fid);
            ctx?.ValidateField(fid);
        };

    private static bool TrySetTyped(ExpressionAccessor.Accessor acc, string raw)
    {
        var t = acc.PropertyType;
        if (Nullable.GetUnderlyingType(t) is not null && string.IsNullOrEmpty(raw))
        {
            acc.Setter(null);
            return true;
        }

        if (t.IsEnum)
        {
            if (Enum.TryParse(t, raw, true, out var en))
            {
                acc.Setter(en);
                return true;
            }

            return false;
        }

        if (RouteValueParser.TryParse(t, raw, out var parsed))
        {
            acc.Setter(parsed);
            return true;
        }

        return false;
    }

    private static IEnumerable<Child>? MarkSelected(IEnumerable<Child>? children, string current)
    {
        if (children is null)
        {
            return null;
        }

        var list = new List<Child>();
        foreach (var c in children)
        {
            if (c.Component is F.Option opt)
            {
                list.Add(MarkOption(opt, current));
            }
            else if (c.Component is F.Optgroup og)
            {
                list.Add(MarkOptgroup(og, current));
            }
            else
            {
                list.Add(c);
            }
        }

        return list;
    }

    private static F.Option MarkOption(F.Option opt, string current)
    {
        var props = OptionProps(opt);
        if (props is null || props.Selected || props.Value != current)
        {
            return opt;
        }

        var newProps = props with { Selected = true };
        return new F.Option(newProps, OptionChildren(opt));
    }

    private static F.Optgroup MarkOptgroup(F.Optgroup og, string current)
    {
        var children = OptgroupChildren(og);
        if (children is null)
        {
            return og;
        }

        var newChildren = children.Select(c =>
            c.Component is F.Option o ? (Child)MarkOption(o, current) : c).ToArray();
        return new F.Optgroup(OptgroupProps(og), newChildren);
    }

    private static F.Option.Props? OptionProps(F.Option opt) => opt.PropsInternal;
    private static IEnumerable<Child> OptionChildren(F.Option opt) => opt.ChildrenInternal;
    private static F.Optgroup.Props? OptgroupProps(F.Optgroup og) => og.PropsInternal;
    private static IEnumerable<Child> OptgroupChildren(F.Optgroup og) => og.ChildrenInternal;
}
