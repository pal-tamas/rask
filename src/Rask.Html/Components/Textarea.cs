using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Html.Components;

// Generic <textarea> form control implementing IFormControl<T>: the generator synthesizes a controlled
// factory (Value/OnChange) and a Bind-first bound factory (validator fanned none/sync/async). The bound
// value type T is usually string; non-string T round-trips through FormatValue (T→string) and the binding
// parser (string→T). Binding is resolved at render time (WriteAttributes) rather than in a `Bound` factory:
// the textarea's text content is the value, emitted as a child text node by RenderChildren.
//
// Plain usage stays `Textarea<string>(Name: …)[content]` — the value comes from Children; bound/controlled
// usage derives it from Bind/Value.

/// <summary>
///     A multi-line text field. Unlike <c>input</c>, its value is its text content, and it is resizable by
///     default — <c>Rows</c> and <c>Cols</c> only set the initial size.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/textarea">MDN</see>
/// </summary>
public sealed partial class Textarea<T> : Element, IFormControl<T>
{
    protected override string TagName => "textarea";

    /// <summary>The name submitted with the form.</summary>
    public string? Name { get; set; }

    /// <summary>The visible number of text lines.</summary>
    public int? Rows { get; set; }

    /// <summary>The visible width in average character widths.</summary>
    public int? Cols { get; set; }

    /// <summary>A hint shown while the field is empty. Not a substitute for a <c>label</c>.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The form will not submit while this field is empty.</summary>
    public bool? Required { get; set; }

    /// <summary>Makes the control non-interactive and excludes it from submission.</summary>
    public bool? Disabled { get; set; }

    /// <summary>The value cannot be edited but is still focusable and still submitted.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>The most characters the user may enter.</summary>
    public int? MaxLength { get; set; }

    /// <summary>The fewest characters the value may have to be valid.</summary>
    public int? MinLength { get; set; }

    /// <summary>
    ///     How the text is wrapped on submission: <c>soft</c> (the default, no inserted breaks) or
    ///     <c>hard</c>, which needs <c>Cols</c>.
    /// </summary>
    public string? Wrap { get; set; }

    /// <summary>Focuses this control on page load.</summary>
    public bool? Autofocus { get; set; }

    /// <summary>The kind of value expected, so the browser can fill it.</summary>
    public string? Autocomplete { get; set; }

    /// <summary>The <c>id</c> of the form this control belongs to.</summary>
    public new string? Form { get; set; }

    /// <summary>The name under which the field's text direction is submitted alongside its value.</summary>
    public string? Dirname { get; set; }

    // Per-keystroke DOM handler (a textarea is inherently string-valued); not part of IFormControl.
    /// <summary>
    ///     Called on every keystroke with the current text — the hook for a character counter or an
    ///     autosizing textarea. It fires mid-word, so debounce anything that costs more than a render.
    /// </summary>
    public Action<string>? OnInput { get; set; }

    /// <summary>The <see langword="async" /> form of <see cref="OnInput" />.</summary>
    public Func<string, Task>? OnInputAsync { get; set; }

    // IFormControl<T> — bound mode.

    /// <summary>
    ///     The model field this control is bound to, as an expression such as <c>() => model.Notes</c>.
    /// </summary>
    public Expression<Func<T>>? Bind { get; set; }

    /// <summary>A synchronous check run on the bound value, returning an error message or null.</summary>
    public Validate<T>? Validate { get; set; }

    /// <summary>An asynchronous check run on the bound value.</summary>
    public ValidateAsync<T>? ValidateAsync { get; set; }

    /// <summary>Runs after a successful bind, once the model has the new value.</summary>
    public Action<T>? AfterBind { get; set; }

    /// <summary>Runs after a successful bind, asynchronously.</summary>
    public Func<T, Task>? AfterBindAsync { get; set; }

    // IFormControl<T> — controlled mode.

    /// <summary>The control's current value. Prefer <c>Bind</c>.</summary>
    public T? Value { get; set; }
    public Action<T>? OnChange { get; set; }
    public Func<T, Task>? OnChangeAsync { get; set; }

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
            var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind, AfterBindAsync);
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
