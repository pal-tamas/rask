using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;
using C = Rask.Core.Components.Components;

namespace Rask.Core.Components;

public sealed class Input : Element
{
    // Expression-driven factory. The generator picks up [GenerateForwarderFactory] and emits
    // `Components.Input<TProp>(Expression<Func<TProp>> Bind, …)` forwarding here, so callers
    // write `Input(Bind: () => model.Name)` and get type-aware binding with auto-named field,
    // per-type input type (checkbox/number/date/text), and per-type change handlers wired
    // into the ambient EditContext.
    [GenerateForwarderFactory]
    public static Input Bound<TProp>(
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
        Delegate? Validate = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var resolvedType = Type ?? BindingHelpers.DefaultInputType(acc.PropertyType);
        var name = Name ?? acc.PropertyName;

        // Always call Register — null clears a stale delegate from a prior render so dropping
        // the parameter between frames doesn't leave the old rule running.
        ctx?.RegisterFieldValidator(fid, Validate, () => acc.Getter());

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
                OnChangeAsync: BindingHelpers.BoolToggleHandler(acc, ctx, fid, isChecked),
                Id: Id, Class: Class, Style: Style, Data: Data);
        }

        var stringValue = BindingHelpers.FormatValue(current);
        var isImmediate = BindingHelpers.IsImmediateUpdateType(acc.PropertyType);

        return C.Input(
            Type: resolvedType, Name: name, Value: stringValue, Placeholder: Placeholder,
            Required: Required, Disabled: Disabled, ReadOnly: ReadOnly,
            Min: Min, Max: Max, Step: Step, Pattern: Pattern,
            Size: Size, MaxLength: MaxLength, MinLength: MinLength,
            Autocomplete: Autocomplete, Autofocus: Autofocus, List: List,
            OnInputAsync: isImmediate ? BindingHelpers.StringSetHandler(acc, ctx, fid, false) : null,
            OnChangeAsync: BindingHelpers.TouchAndValidateHandler(acc, ctx, fid, !isImmediate),
            Id: Id, Class: Class, Style: Style, Data: Data);
    }

    protected override string TagName => "input";
    protected override bool SelfClosing => true;

    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public bool ReadOnly { get; set; }
    public bool Checked { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public string? Step { get; set; }
    public string? Pattern { get; set; }
    public int? Size { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool Multiple { get; set; }
    public string? Accept { get; set; }
    public string? Alt { get; set; }
    public string? Autocomplete { get; set; }
    public bool Autofocus { get; set; }
    public string? Form { get; set; }
    public string? FormAction { get; set; }
    public string? FormEnctype { get; set; }
    public string? FormMethod { get; set; }
    public bool FormNovalidate { get; set; }
    public string? FormTarget { get; set; }
    public string? List { get; set; }
    public string? Src { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public Action<string>? OnInput { get; set; }
    public Action<string>? OnChange { get; set; }
    public Func<string, Task>? OnInputAsync { get; set; }
    public Func<string, Task>? OnChangeAsync { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Type is not null) yield return new("type", Type);
        if (Name is not null) yield return new("name", Name);
        if (Value is not null) yield return new("value", Value);
        if (Placeholder is not null) yield return new("placeholder", Placeholder);
        if (Required) yield return new("required", null);
        if (Disabled) yield return new("disabled", null);
        if (ReadOnly) yield return new("readonly", null);
        if (Checked) yield return new("checked", null);
        if (Min is not null) yield return new("min", Min);
        if (Max is not null) yield return new("max", Max);
        if (Step is not null) yield return new("step", Step);
        if (Pattern is not null) yield return new("pattern", Pattern);
        if (Size is not null) yield return new("size", Size.Value.ToString(CultureInfo.InvariantCulture));
        if (MaxLength is not null) yield return new("maxlength", MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        if (MinLength is not null) yield return new("minlength", MinLength.Value.ToString(CultureInfo.InvariantCulture));
        if (Multiple) yield return new("multiple", null);
        if (Accept is not null) yield return new("accept", Accept);
        if (Alt is not null) yield return new("alt", Alt);
        if (Autocomplete is not null) yield return new("autocomplete", Autocomplete);
        if (Autofocus) yield return new("autofocus", null);
        if (Form is not null) yield return new("form", Form);
        if (FormAction is not null) yield return new("formaction", FormAction);
        if (FormEnctype is not null) yield return new("formenctype", FormEnctype);
        if (FormMethod is not null) yield return new("formmethod", FormMethod);
        if (FormNovalidate) yield return new("formnovalidate", null);
        if (FormTarget is not null) yield return new("formtarget", FormTarget);
        if (List is not null) yield return new("list", List);
        if (Src is not null) yield return new("src", Src);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));

        if (LiveRenderContext.Current is { } ctx)
        {
            var input = (Delegate?)OnInput ?? OnInputAsync;
            if (input is not null) yield return new("data-rask-on-input", ctx.RegisterHandler(input));

            var change = (Delegate?)OnChange ?? OnChangeAsync;
            if (change is not null) yield return new("data-rask-on-change", ctx.RegisterHandler(change));
        }
    }
}
