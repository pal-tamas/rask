using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;
using RaskFileType = Rask.Core.Forms.RaskFile;

namespace Rask.Core.Components;

public sealed class Input : Element
{
    protected override string TagName => "input";
    protected override bool SelfClosing => true;

    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public bool? Required { get; set; }
    public bool? Disabled { get; set; }
    public bool? ReadOnly { get; set; }
    public bool? Checked { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public string? Step { get; set; }
    public string? Pattern { get; set; }
    public int? Size { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool? Multiple { get; set; }
    public string? Accept { get; set; }
    public string? Alt { get; set; }
    public string? Autocomplete { get; set; }
    public bool? Autofocus { get; set; }
    public string? Form { get; set; }
    public string? FormAction { get; set; }
    public string? FormEnctype { get; set; }
    public string? FormMethod { get; set; }
    public bool? FormNovalidate { get; set; }
    public string? FormTarget { get; set; }
    public string? List { get; set; }
    public string? Src { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public Callback<string>? OnInput { get; set; }
    public Callback<string>? OnChange { get; set; }
    public CallbackAsync<string>? OnInputAsync { get; set; }
    public CallbackAsync<string>? OnChangeAsync { get; set; }
    public Callback<IReadOnlyList<RaskFileType>>? OnFiles { get; set; }

    public CallbackAsync<IReadOnlyList<RaskFileType>>? OnFilesAsync { get; set; }

    // Expression-driven factory. The generator picks up [GenerateForwarderFactory] and emits
    // `Components.Input<TProp>(Expression<Func<TProp>> Bind, …)` forwarding here, so callers
    // write `Input(Bind: () => model.Name)` and get type-aware binding with auto-named field,
    // per-type input type (checkbox/number/date/text), and per-type change handlers wired
    // into the ambient EditContext.
    //
    // `Validate` ships as three overloads to dodge the delegate cast at the call site:
    //   - no Validate parameter at all (omit to skip validation),
    //   - typed sync   `Validate<TProp> Validate`,
    //   - typed async  `ValidateAsync<TProp> Validate`.
    // Both are the shared validator delegate types in Rask.Core.Forms.
    // Overload resolution picks based on the lambda's arity; all three forward to the shared
    // BoundCore which collapses to the single `Delegate?` the EditContext consumes.
    [GenerateForwarderFactory]
    public static Input Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Action<TProp>? AfterBind = null,
        Func<TProp, Task>? AfterBindAsync = null,
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
        => BoundCore(Bind, Type, Name, Placeholder, Required, Disabled, ReadOnly,
            Min, Max, Step, Pattern, Size, MaxLength, MinLength,
            Autocomplete, Autofocus, List, null,
            AfterBind, AfterBindAsync, Id, Class, Style, Data);

    [GenerateForwarderFactory]
    public static Input Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Validate<TProp> Validate,
        Action<TProp>? AfterBind = null,
        Func<TProp, Task>? AfterBindAsync = null,
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
        => BoundCore(Bind, Type, Name, Placeholder, Required, Disabled, ReadOnly,
            Min, Max, Step, Pattern, Size, MaxLength, MinLength,
            Autocomplete, Autofocus, List, Validate,
            AfterBind, AfterBindAsync, Id, Class, Style, Data);

    [GenerateForwarderFactory]
    public static Input Bound<TProp>(
        Expression<Func<TProp>> Bind,
        ValidateAsync<TProp> Validate,
        Action<TProp>? AfterBind = null,
        Func<TProp, Task>? AfterBindAsync = null,
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
        => BoundCore(Bind, Type, Name, Placeholder, Required, Disabled, ReadOnly,
            Min, Max, Step, Pattern, Size, MaxLength, MinLength,
            Autocomplete, Autofocus, List, Validate,
            AfterBind, AfterBindAsync, Id, Class, Style, Data);

    private static Input BoundCore<TProp>(
        Expression<Func<TProp>> Bind,
        string? Type,
        string? Name,
        string? Placeholder,
        bool Required,
        bool Disabled,
        bool ReadOnly,
        string? Min,
        string? Max,
        string? Step,
        string? Pattern,
        int? Size,
        int? MaxLength,
        int? MinLength,
        string? Autocomplete,
        bool Autofocus,
        string? List,
        Delegate? validate,
        Action<TProp>? afterBindSync,
        Func<TProp, Task>? afterBindAsync,
        string? Id,
        string? Class,
        string? Style,
        IReadOnlyDictionary<string, string?>? Data)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var resolvedType = Type ?? BindingHelpers.DefaultInputType(acc.PropertyType);
        var name = Name ?? acc.PropertyName;

        // Always call Register — null clears a stale delegate from a prior render so dropping
        // the parameter between frames doesn't leave the old rule running.
        ctx?.RegisterFieldValidator(fid, validate, () => acc.Getter());

        var afterBind = BindingHelpers.BuildAfterBind(acc, afterBindSync, afterBindAsync);
        var current = acc.Getter();

        if (resolvedType == "checkbox")
        {
            var isChecked = current is bool b && b;
            return C.Input(
                "checkbox", name,
                Checked: isChecked,
                Required: Required, Disabled: Disabled, ReadOnly: ReadOnly,
                Min: Min, Max: Max, Step: Step, Pattern: Pattern,
                Size: Size, MaxLength: MaxLength, MinLength: MinLength,
                Autocomplete: Autocomplete, Autofocus: Autofocus, List: List,
                OnChangeAsync: BindingHelpers.BoolSetHandler(acc, ctx, fid, afterBind),
                Id: Id, Class: Class, Style: Style, Data: Data);
        }

        var stringValue = BindingHelpers.FormatValue(current);
        var isImmediate = BindingHelpers.IsImmediateUpdateType(acc.PropertyType);

        return C.Input(
            resolvedType, name, stringValue, Placeholder,
            Required, Disabled, ReadOnly,
            Min: Min, Max: Max, Step: Step, Pattern: Pattern,
            Size: Size, MaxLength: MaxLength, MinLength: MinLength,
            Autocomplete: Autocomplete, Autofocus: Autofocus, List: List,
            OnInputAsync: isImmediate ? BindingHelpers.StringSetHandler(acc, ctx, fid, false, afterBind) : null,
            OnChangeAsync: BindingHelpers.TouchAndValidateHandler(acc, ctx, fid, !isImmediate, afterBind),
            Id: Id, Class: Class, Style: Style, Data: Data);
    }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }

        if (Placeholder is not null)
        {
            AppendAttr(sb, "placeholder", Placeholder);
        }

        if (Required is true)
        {
            AppendAttr(sb, "required", null);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (ReadOnly is true)
        {
            AppendAttr(sb, "readonly", null);
        }

        if (Checked is true)
        {
            AppendAttr(sb, "checked", null);
        }

        if (Min is not null)
        {
            AppendAttr(sb, "min", Min);
        }

        if (Max is not null)
        {
            AppendAttr(sb, "max", Max);
        }

        if (Step is not null)
        {
            AppendAttr(sb, "step", Step);
        }

        if (Pattern is not null)
        {
            AppendAttr(sb, "pattern", Pattern);
        }

        if (Size is not null)
        {
            AppendAttr(sb, "size", Size.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (MaxLength is not null)
        {
            AppendAttr(sb, "maxlength", MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (MinLength is not null)
        {
            AppendAttr(sb, "minlength", MinLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Multiple is true)
        {
            AppendAttr(sb, "multiple", null);
        }

        if (Accept is not null)
        {
            AppendAttr(sb, "accept", Accept);
        }

        if (Alt is not null)
        {
            AppendAttr(sb, "alt", Alt);
        }

        if (Autocomplete is not null)
        {
            AppendAttr(sb, "autocomplete", Autocomplete);
        }

        if (Autofocus is true)
        {
            AppendAttr(sb, "autofocus", null);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (FormAction is not null)
        {
            AppendUrlAttr(sb, "formaction", FormAction);
        }

        if (FormEnctype is not null)
        {
            AppendAttr(sb, "formenctype", FormEnctype);
        }

        if (FormMethod is not null)
        {
            AppendAttr(sb, "formmethod", FormMethod);
        }

        if (FormNovalidate is true)
        {
            AppendAttr(sb, "formnovalidate", null);
        }

        if (FormTarget is not null)
        {
            AppendAttr(sb, "formtarget", FormTarget);
        }

        if (List is not null)
        {
            AppendAttr(sb, "list", List);
        }

        if (Src is not null)
        {
            AppendMediaUrlAttr(sb, "src", Src);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (LiveRenderContext.CurrentSync is { } ctx)
        {
            var input = (Delegate?)OnInput ?? OnInputAsync;
            if (input is not null)
            {
                AppendAttr(sb, "data-rask-on-input", ctx.RegisterHandler(input));
            }

            var change = (Delegate?)OnChange ?? OnChangeAsync;
            if (change is not null)
            {
                AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(change));
            }

            var files = (Delegate?)OnFiles ?? OnFilesAsync;
            if (files is not null)
            {
                AppendAttr(sb, "data-rask-on-files", ctx.RegisterHandler(files));
            }
        }
    }
}
