using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Routing;
using F = Rask.Core.Components;
using C = Rask.Core.Components.Components;

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
        var ctx = ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var resolvedType = Type ?? DefaultInputType(acc.PropertyType);
        var name = Name ?? acc.PropertyName;

        var current = acc.Getter();

        if (resolvedType == "checkbox")
        {
            var isChecked = current is bool b && b;
            return C.Input(
                Type: "checkbox", Name: name,
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

        return C.Input(
            Type: resolvedType, Name: name, Value: stringValue, Placeholder: Placeholder,
            Required: Required, Disabled: Disabled, ReadOnly: ReadOnly,
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
        var ctx = ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var name = Name ?? acc.PropertyName;
        var stringValue = FormatValue(acc.Getter());

        return C.Textarea(
            Name: name, Rows: Rows, Cols: Cols, Placeholder: Placeholder,
            Required: Required, Disabled: Disabled, ReadOnly: ReadOnly,
            MaxLength: MaxLength, MinLength: MinLength, Wrap: Wrap,
            Autofocus: Autofocus, Autocomplete: Autocomplete,
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
        params IEnumerable<Child> Children)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var name = Name ?? acc.PropertyName;
        var current = FormatValue(acc.Getter());
        var preselected = MarkSelected(Children, current);

        return C.Select(
            Name: name, Required: Required, Disabled: Disabled, Size: Size,
            Autofocus: Autofocus, Autocomplete: Autocomplete,
            OnChange: TouchAndValidateHandler(acc, ctx, fid, true),
            Id: Id, Class: Class, Style: Style, Data: Data,
            Children: preselected);
    }

    public static F.ValidationMessage ValidationMessage<TProp>(
        Expression<Func<TProp>> For,
        string? Class = null) => new() { For = For, Class = Class };

    public static F.ValidationSummary ValidationSummary(string? Class = null) => new() { Class = Class };

    // Form pushes its EditContext onto EditContextScope only during *serialization* of
    // its children (HtmlSerializer enters Form.EnterChildrenScope after Render returns).
    // Input<TProp>/Textarea<TProp>/Select<TProp> factories, however, run during the
    // parent's Render() — before serialization — so EditContextScope.Current is still
    // null at that point. Falling back to LiveRenderContext.GetOrCreateEditContext
    // (the same lookup Form.ResolveContext uses, keyed by model reference) yields the
    // identical EditContext instance Form will later receive, so per-field
    // NotifyFieldChanged / ValidateField calls from the input's handlers land in the
    // right context.
    private static EditContext? ResolveBindingContext(object model) =>
        EditContextScope.Current ?? LiveRenderContext.Current?.GetOrCreateEditContext(model);

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
            if (!TrySetTyped(acc, raw))
            {
                return;
            }

            ctx?.NotifyFieldChanged(fid);
            // Blazor parity: stay quiet until the user (or a submit) has touched the field,
            // then re-validate on every keystroke so a correction clears the message
            // without needing a blur to trigger the change event.
            if (ctx is not null && (validateOnSet || ctx.IsTouched(fid)))
            {
                ctx.ValidateField(fid);
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

    private static IEnumerable<Child> MarkSelected(IEnumerable<Child> children, string current)
    {
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
        if (opt.Selected || opt.Value != current)
        {
            return opt;
        }

        return new F.Option
        {
            Value = opt.Value,
            Selected = true,
            Disabled = opt.Disabled,
            Label = opt.Label,
            Id = opt.Id,
            Class = opt.Class,
            Style = opt.Style,
            Data = opt.Data,
            Children = opt.Children
        };
    }

    private static F.Optgroup MarkOptgroup(F.Optgroup og, string current)
    {
        if (og.Children is null)
        {
            return og;
        }

        var newChildren = og.Children.Select(c =>
            c.Component is F.Option o ? (Child)MarkOption(o, current) : c).ToArray();
        return new F.Optgroup
        {
            Disabled = og.Disabled,
            Label = og.Label,
            Id = og.Id,
            Class = og.Class,
            Style = og.Style,
            Data = og.Data,
            Children = newChildren
        };
    }
}
