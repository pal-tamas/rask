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
// the expression: `Input.Bind(() => model.Age)` → Input<int> → <input type="number">.

/// <summary>
///     A form control, whose <c>Type</c> decides everything about it. In Rask it is generic in the bound
///     value's type: <c>Bind</c> takes an expression naming the field, and the framework parses, validates
///     and writes back the value for you. Every input needs a <c>label</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/input">MDN</see>
/// </summary>
public sealed class Input<T> : Element, IFormControl<T>
{
    protected override string TagName => "input";
    protected override bool SelfClosing => true;

    /// <summary>
    ///     Which control this is — text, checkbox, date, file, and so on. Choosing the right one gets you
    ///     the right mobile keyboard and the browser's own validation for free.
    /// </summary>
    public InputType? Type { get; set; }

    /// <summary>The name submitted with the form.</summary>
    public string? Name { get; set; }

    // IFormControl<T> controlled value — kept at the legacy `Value` position so positional factory calls
    // (Input<string>("text", "name", "value", …)) keep their argument order.

    /// <summary>
    ///     The control's current value. Prefer <c>Bind</c>, which keeps it in step with your model in both
    ///     directions.
    /// </summary>
    public T? Value { get; set; }

    /// <summary>
    ///     A hint shown while the field is empty. Never a substitute for a <c>label</c> — it disappears the
    ///     moment the user types.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>The form will not submit while this field is empty.</summary>
    public bool? Required { get; set; }

    /// <summary>
    ///     Makes the control non-interactive and excludes it from submission. Use <c>ReadOnly</c> when the
    ///     value should still be submitted.
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>The value cannot be edited but is still focusable and still submitted.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>Whether a checkbox or radio starts checked.</summary>
    /// <remarks>
    ///     Controlled mode only — it is the checkbox's value. A bound control derives the checked state
    ///     from the model, so this is neither a step on a bound chain nor a parameter of the bound factory.
    /// </remarks>
    public bool? Checked { get; set; }

    /// <summary>The smallest permitted value, for numeric and date types.</summary>
    public string? Min { get; set; }

    /// <summary>The largest permitted value, for numeric and date types.</summary>
    public string? Max { get; set; }

    /// <summary>The granularity the value must snap to; <c>any</c> removes the restriction.</summary>
    public string? Step { get; set; }

    /// <summary>
    ///     A regular expression the value must match. Give a <c>Title</c> as well — it is what the browser
    ///     shows when the pattern fails.
    /// </summary>
    public new string? Pattern { get; set; }

    /// <summary>The control's visible width in characters. A display hint, not a limit.</summary>
    public int? Size { get; set; }

    /// <summary>The most characters the user may enter.</summary>
    public int? MaxLength { get; set; }

    /// <summary>The fewest characters the value may have to be valid.</summary>
    public int? MinLength { get; set; }

    /// <summary>Allows more than one value, for <c>file</c> and <c>email</c>.</summary>
    public bool? Multiple { get; set; }

    /// <summary>
    ///     Which file types a file picker should offer — extensions, MIME types, or <c>image/*</c>. A
    ///     filter, not a guarantee: validate on the server.
    /// </summary>
    public string? Accept { get; set; }

    /// <summary>
    ///     The alternative text for an <c>image</c>-type input, which is a button and needs one.
    /// </summary>
    public string? Alt { get; set; }

    /// <summary>
    ///     The kind of value expected (<c>email</c>, <c>current-password</c>, <c>one-time-code</c>), so the
    ///     browser can fill it. Getting this right is a real usability win; <c>off</c> is widely ignored.
    /// </summary>
    public string? Autocomplete { get; set; }

    /// <summary>
    ///     Focuses this control on page load. At most one per page, and it disorients screen-reader users —
    ///     reserve it for a page whose only purpose is this field.
    /// </summary>
    public bool? Autofocus { get; set; }

    /// <summary>The <c>id</c> of the form this control belongs to, for a control outside it.</summary>
    public new string? Form { get; set; }

    /// <summary>Overrides the form's <c>action</c> when this control submits it.</summary>
    public string? FormAction { get; set; }

    /// <summary>Overrides the form's encoding when this control submits it.</summary>
    public string? FormEnctype { get; set; }

    /// <summary>Overrides the form's HTTP method when this control submits it.</summary>
    public string? FormMethod { get; set; }

    /// <summary>Skips validation when this control submits the form.</summary>
    public bool? FormNovalidate { get; set; }

    /// <summary>Overrides the form's <c>target</c> when this control submits it.</summary>
    public string? FormTarget { get; set; }

    /// <summary>The <c>id</c> of a <c>datalist</c> supplying autocomplete suggestions.</summary>
    public string? List { get; set; }

    /// <summary>The image URL for an <c>image</c>-type input.</summary>
    public string? Src { get; set; }

    /// <summary>The image width for an <c>image</c>-type input.</summary>
    public int? Width { get; set; }

    /// <summary>The image height for an <c>image</c>-type input.</summary>
    public int? Height { get; set; }

    // Mobile / accessibility hints. InputMode picks the on-screen keyboard (numeric/decimal/email/…),
    // EnterKeyHint labels its action key (done/go/search/…), Spellcheck toggles the enumerated
    // spellcheck attribute ("true"/"false"), Capture asks a file input for the camera/mic ("user"/
    // "environment"), and Dirname submits the field's text direction alongside its value.

    /// <summary>
    ///     Which virtual keyboard to show — <c>numeric</c>, <c>decimal</c>, <c>tel</c>, <c>search</c>.
    ///     Changes the keyboard without changing validation.
    /// </summary>
    public string? InputMode { get; set; }

    /// <summary>
    ///     What the virtual keyboard's action key should say: <c>enter</c>, <c>done</c>, <c>go</c>,
    ///     <c>next</c>, <c>search</c>, <c>send</c>.
    /// </summary>
    public string? EnterKeyHint { get; set; }

    /// <summary>Whether the browser should spell-check the value.</summary>
    public bool? Spellcheck { get; set; }

    /// <summary>
    ///     For a file input, asks for the camera or microphone directly: <c>user</c> for the front camera,
    ///     <c>environment</c> for the rear.
    /// </summary>
    public string? Capture { get; set; }

    /// <summary>The name under which the field's text direction is submitted alongside its value.</summary>
    public string? Dirname { get; set; }

    // DOM event handlers in the legacy declaration order so positional factory calls keep working.
    // OnChange/OnChangeAsync are the IFormControl<T> controlled callbacks (typed T); OnInput/OnFiles are the
    // string/file DOM handlers, not part of the interface.
    // Calling one back is `OnInput?.Invoke(value)`. OnFiles is NOT controlled-mode only — bound mode
    // still wires that one.
    /// <summary>
    ///     Called on every keystroke with the raw text of the field, before any parsing — so it is the
    ///     hook for live search and character counters. Unlike <see cref="OnChange" /> it is a
    ///     <see langword="string" /> whatever <typeparamref name="T" /> is, and it fires while the value
    ///     is still half-typed and possibly not valid.
    /// </summary>
    /// <remarks>
    ///     Controlled mode only: a bound control installs its own <c>oninput</c> write-back and never
    ///     reads this, so it is neither a step on a bound chain nor a parameter of the bound factory. Use
    ///     <see cref="AfterBind" /> for a side effect on each bound write.
    /// </remarks>
    public Action<string>? OnInput { get; set; }

    /// <summary>
    ///     Called with the parsed value once the user commits a change, in controlled mode. Store it and
    ///     pass it back through <c>Value</c>.
    /// </summary>
    public Action<T>? OnChange { get; set; }

    /// <summary>The <see langword="async" /> form of <see cref="OnInput" />.</summary>
    public Func<string, Task>? OnInputAsync { get; set; }

    /// <summary>The <see langword="async" /> form of <see cref="OnChange" />.</summary>
    public Func<T, Task>? OnChangeAsync { get; set; }

    /// <summary>
    ///     Called with the chosen files when this is a file input. The list is empty when the user cancels
    ///     the picker, so check it before reading the first entry.
    ///     <para>
    ///         Never trust what arrives: a file's reported name, size and type all come from the client.
    ///         Re-check them on the server before storing anything.
    ///     </para>
    /// </summary>
    public Action<IReadOnlyList<RaskFileType>>? OnFiles { get; set; }

    /// <summary>The <see langword="async" /> form of <see cref="OnFiles" /> — for reading or uploading.</summary>
    public Func<IReadOnlyList<RaskFileType>, Task>? OnFilesAsync { get; set; }

    // IFormControl<T> — bound mode (excluded from the controlled factory by the generator).

    /// <summary>
    ///     The model field this control is bound to, as an expression such as <c>() => model.Email</c>.
    ///     Rask reads the value, writes edits back, and infers the parse and the validation from the
    ///     field's type.
    /// </summary>
    public Expression<Func<T>>? Bind { get; set; }

    /// <summary>A synchronous check run on the bound value, returning an error message or null.</summary>
    public Validate<T>? Validate { get; set; }

    /// <summary>An asynchronous check run on the bound value — a uniqueness lookup, say.</summary>
    public ValidateAsync<T>? ValidateAsync { get; set; }

    /// <summary>Runs after a successful bind, once the model has the new value.</summary>
    public Action<T>? AfterBind { get; set; }

    /// <summary>Runs after a successful bind, asynchronously.</summary>
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
