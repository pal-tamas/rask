using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

// Generic <textarea> form control implementing IFormControl<T>: the generator synthesizes a controlled
// factory (Value/OnChange) and a Bind-first bound factory (validator fanned none/sync/async). The bound
// value type T is usually string; non-string T round-trips through FormatValue (T→string) and the binding
// parser (string→T). Binding is resolved at render time (WriteAttributes) rather than in a `Bound` factory:
// the textarea's text content is the value, emitted as a child text node by RenderChildren.
//
// Plain usage stays `Textarea<string>(Name: …)[content]` — the value comes from Children; bound/controlled
// usage derives it from Bind/Value.
public sealed class Textarea<T> : Element, IFormControl<T>
{
    protected override string TagName => "textarea";

    public string? Name { get; set; }
    public int? Rows { get; set; }
    public int? Cols { get; set; }
    public string? Placeholder { get; set; }
    public bool? Required { get; set; }
    public bool? Disabled { get; set; }
    public bool? ReadOnly { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public string? Wrap { get; set; }
    public bool? Autofocus { get; set; }
    public string? Autocomplete { get; set; }
    public new string? Form { get; set; }
    public string? Dirname { get; set; }

    // Per-keystroke DOM handler (a textarea is inherently string-valued); not part of IFormControl.
    public Callback<string>? OnInput { get; set; }
    public CallbackAsync<string>? OnInputAsync { get; set; }

    // IFormControl<T> — bound mode.
    public Expression<Func<T>>? Bind { get; set; }
    public Carrier<Validate<T>>? Validate { get; set; }
    public Carrier<ValidateAsync<T>>? ValidateAsync { get; set; }
    public Carrier<Action<T>>? AfterBind { get; set; }
    public Carrier<Func<T, Task>>? AfterBindAsync { get; set; }

    // IFormControl<T> — controlled mode.
    public T? Value { get; set; }
    public Callback<T>? OnChange { get; set; }
    public CallbackAsync<T>? OnChangeAsync { get; set; }

    // The rendered text content, resolved in WriteAttributes (bound/controlled) and emitted by
    // RenderChildren. Null leaves the plain Children content (indexer) in place.
    private string? _content;

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        // Bound mode parses the expression up front so the auto-derived `name` lands in attribute order.
        ExpressionAccessor.Accessor? acc = null;
        EditContext? bindCtx = null;
        var fid = default(FieldIdentifier);
        if (Bind is not null)
        {
            acc = ExpressionAccessor.Parse(Bind);
            bindCtx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            _content = BindingHelpers.FormatValue(acc.Getter());
        }
        else if (Value is not null)
        {
            _content = BindingHelpers.FormatValue(Value);
        }

        var name = Name ?? acc?.PropertyName;
        if (name is not null)
        {
            AppendAttr(sb, "name", name);
        }

        if (Rows is not null)
        {
            AppendAttr(sb, "rows", Rows.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Cols is not null)
        {
            AppendAttr(sb, "cols", Cols.Value.ToString(CultureInfo.InvariantCulture));
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

        if (MaxLength is not null)
        {
            AppendAttr(sb, "maxlength", MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (MinLength is not null)
        {
            AppendAttr(sb, "minlength", MinLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Wrap is not null)
        {
            AppendAttr(sb, "wrap", Wrap);
        }

        if (Autofocus is true)
        {
            AppendAttr(sb, "autofocus", null);
        }

        if (Autocomplete is not null)
        {
            AppendAttr(sb, "autocomplete", Autocomplete);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (Dirname is not null)
        {
            AppendAttr(sb, "dirname", Dirname);
        }

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        if (acc is not null)
        {
            // Bound: write the model on input, touch + revalidate on change.
            var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind?.Fn, AfterBindAsync?.Fn);
            ((IFormControl<T>)this).RegisterValidator(acc, bindCtx);
            AppendAttr(sb, "data-rask-on-input",
                ctx.RegisterHandler(BindingHelpers.StringSetHandler(acc, bindCtx, fid, false, afterBind)));
            AppendAttr(sb, "data-rask-on-change",
                ctx.RegisterHandler(BindingHelpers.TouchAndValidateHandler(acc, bindCtx, fid, false)));
            return;
        }

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

    protected override IEnumerable<Component?> RenderChildren() =>
        _content is not null ? [_content] : base.RenderChildren();
}
