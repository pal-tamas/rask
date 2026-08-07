using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;
using RaskFileType = Rask.Core.Forms.RaskFile;

namespace Rask.Core.Components;

// Generic <input> form control implementing IFormControl<T>. The generator synthesizes a controlled/plain
// factory (returning Input<string> — see the plain-factory note below) and a Bind-first bound factory
// (validator fanned none/sync/async). Binding is resolved at render time (WriteAttributes): the HTML input
// `type`, the value/checked state, and the change handlers are derived from the bound value type T and the
// expression, rather than eagerly in a `Bound` factory.
//
// Type derivation from T: bool→checkbox, numeric→number, DateOnly→date, DateTime(Offset)→datetime-local,
// TimeOnly/TimeSpan→time, everything else→text. In plain/controlled mode a string T keeps today's behavior
// (no `type` attribute unless Type is set); bound mode and non-string T default the type from T.
//
// Plain usage stays string-shaped: `Input<string>("text", Value: …, OnInput: …)`. Bound usage infers T from
// the expression: `Input(() => model.Age)` → Input<int> → <input type="number">.
public sealed class Input<T> : Element, IFormControl<T>
{
    protected override string TagName => "input";
    protected override bool SelfClosing => true;

    public InputType? Type { get; set; }
    public string? Name { get; set; }

    // IFormControl<T> controlled value — kept at the legacy `Value` position so positional factory calls
    // (Input<string>("text", "name", "value", …)) keep their argument order.
    public T? Value { get; set; }
    public string? Placeholder { get; set; }
    public bool? Required { get; set; }
    public bool? Disabled { get; set; }
    public bool? ReadOnly { get; set; }
    public bool? Checked { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public string? Step { get; set; }
    public new string? Pattern { get; set; }
    public int? Size { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool? Multiple { get; set; }
    public string? Accept { get; set; }
    public string? Alt { get; set; }
    public string? Autocomplete { get; set; }
    public bool? Autofocus { get; set; }
    public new string? Form { get; set; }
    public string? FormAction { get; set; }
    public string? FormEnctype { get; set; }
    public string? FormMethod { get; set; }
    public bool? FormNovalidate { get; set; }
    public string? FormTarget { get; set; }
    public string? List { get; set; }
    public string? Src { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    // Mobile / accessibility hints. InputMode picks the on-screen keyboard (numeric/decimal/email/…),
    // EnterKeyHint labels its action key (done/go/search/…), Spellcheck toggles the enumerated
    // spellcheck attribute ("true"/"false"), Capture asks a file input for the camera/mic ("user"/
    // "environment"), and Dirname submits the field's text direction alongside its value.
    public string? InputMode { get; set; }
    public string? EnterKeyHint { get; set; }
    public bool? Spellcheck { get; set; }
    public string? Capture { get; set; }
    public string? Dirname { get; set; }

    // DOM event handlers in the legacy declaration order so positional factory calls keep working.
    // OnChange/OnChangeAsync are the IFormControl<T> controlled callbacks (typed T); OnInput/OnFiles are the
    // string/file DOM handlers, not part of the interface.
    public Callback<string>? OnInput { get; set; }
    public Callback<T>? OnChange { get; set; }
    public CallbackAsync<string>? OnInputAsync { get; set; }
    public CallbackAsync<T>? OnChangeAsync { get; set; }
    public Callback<IReadOnlyList<RaskFileType>>? OnFiles { get; set; }
    public CallbackAsync<IReadOnlyList<RaskFileType>>? OnFilesAsync { get; set; }

    // IFormControl<T> — bound mode (excluded from the controlled factory by the generator).
    public Expression<Func<T>>? Bind { get; set; }
    public Validate<T>? Validate { get; set; }
    public ValidateAsync<T>? ValidateAsync { get; set; }
    public Action<T>? AfterBind { get; set; }
    public Func<T, Task>? AfterBindAsync { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        // Resolve binding up front so the auto-derived name + value land in attribute order.
        ExpressionAccessor.Accessor? acc = null;
        EditContext? bindCtx = null;
        var fid = default(FieldIdentifier);
        object? boundValue = null;
        if (Bind is not null)
        {
            acc = ExpressionAccessor.Parse(Bind);
            bindCtx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            boundValue = acc.Getter();
        }

        // Type: an explicit InputType wins; otherwise bound mode (and non-string T) default from T, while a
        // plain string input keeps "no type unless set".
        var resolvedType = Type?.ToHtml();
        if (resolvedType is null && (acc is not null || typeof(T) != typeof(string)))
        {
            resolvedType = BindingHelpers.DefaultInputType(typeof(T));
        }

        var isCheckbox = resolvedType == "checkbox";
        var name = Name ?? acc?.PropertyName;

        // Value / checked state. Bound mode derives one from the model (checkbox → checked, else → value);
        // plain/controlled mode honors the explicit Value/Checked props independently, exactly as before.
        string? valueString = null;
        bool? checkedState = null;
        if (acc is not null)
        {
            if (isCheckbox)
            {
                checkedState = boundValue is bool b && b;
            }
            else
            {
                valueString = BindingHelpers.FormatValue(boundValue);
            }
        }
        else
        {
            checkedState = Checked;
            valueString = Value is not null ? BindingHelpers.FormatValue(Value) : null;
        }

        if (resolvedType is not null)
        {
            AppendAttr(sb, "type", resolvedType);
        }

        if (name is not null)
        {
            AppendAttr(sb, "name", name);
        }

        if (valueString is not null)
        {
            AppendAttr(sb, "value", valueString);
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

        if (checkedState is true)
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

        // An explicit Step always wins. Otherwise a fractional bound type needs step="any": HTML defaults
        // to step="1", so the browser's own constraint validation rejects 42.50 and never fires submit —
        // silently, with no validation message and nothing thrown. Same hazard on a range input.
        var step = Step ?? (resolvedType is "number" or "range" ? BindingHelpers.DefaultStep(typeof(T)) : null);
        if (step is not null)
        {
            AppendAttr(sb, "step", step);
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

        if (Capture is not null)
        {
            AppendAttr(sb, "capture", Capture);
        }

        if (Alt is not null)
        {
            AppendAttr(sb, "alt", Alt);
        }

        if (Autocomplete is not null)
        {
            AppendAttr(sb, "autocomplete", Autocomplete);
        }

        if (InputMode is not null)
        {
            AppendAttr(sb, "inputmode", InputMode);
        }

        if (EnterKeyHint is not null)
        {
            AppendAttr(sb, "enterkeyhint", EnterKeyHint);
        }

        if (Spellcheck is not null)
        {
            AppendAttr(sb, "spellcheck", Spellcheck.Value ? "true" : "false");
        }

        if (Dirname is not null)
        {
            AppendAttr(sb, "dirname", Dirname);
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

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        if (acc is not null)
        {
            // Bound: write the model on input (immediate for string) / change, validate.
            var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind, AfterBindAsync);
            ((IFormControl<T>)this).RegisterValidator(acc, bindCtx);
            if (isCheckbox)
            {
                AppendAttr(sb, "data-rask-on-change",
                    ctx.RegisterHandler(BindingHelpers.BoolSetHandler(acc, bindCtx, fid, afterBind)));
            }
            else
            {
                var immediate = BindingHelpers.IsImmediateUpdateType(typeof(T));
                if (immediate)
                {
                    AppendAttr(sb, "data-rask-on-input",
                        ctx.RegisterHandler(BindingHelpers.StringSetHandler(acc, bindCtx, fid, false, afterBind)));
                }

                AppendAttr(sb, "data-rask-on-change",
                    ctx.RegisterHandler(BindingHelpers.TouchAndValidateHandler(acc, bindCtx, fid, !immediate, afterBind)));
            }
        }
        else
        {
            // Plain / controlled.
            var input = (Delegate?)OnInput ?? OnInputAsync;
            if (input is not null)
            {
                AppendAttr(sb, "data-rask-on-input", ctx.RegisterHandler(input));
            }

            var change = ((IFormControl<T>)this).ControlledChangeHandler();
            if (change is not null)
            {
                AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(change));
            }
        }

        var files = (Delegate?)OnFiles ?? OnFilesAsync;
        if (files is not null)
        {
            AppendAttr(sb, "data-rask-on-files", ctx.RegisterHandler(files));
        }
    }
}
