using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;
using RaskFileType = Rask.Core.Forms.RaskFile;

namespace Rask.Core.Components;

// The typed layer over MDN's HTMLInputElement (generated from mdn.snapshot.json, abstract, every plain
// attribute). What MDN cannot know lives here: the bound value's type T, and what follows from it. The input
// `type`, `name`, `value`/`checked` and a fractional `step` are derived from T and the Bind expression at render
// time, so this layer owns those five attributes and writes them after the generated ones.
//
// Type derivation from T: bool→checkbox, numeric→number, DateOnly→date, DateTime(Offset)→datetime-local,
// TimeOnly/TimeSpan→time, everything else→text. A plain string input keeps "no type unless set"; bound mode
// and non-string T default the type from T. `Input.Bind(() => model.Age)` → HTMLInputElement<int>.

/// <summary>
///     An <c>&lt;input&gt;</c> typed by the value it edits: <c>Bind</c> takes an expression naming the field, and
///     Rask parses, validates and writes back the value for you. Every input needs a <c>label</c>.
/// </summary>
public sealed partial class HTMLInputElement<T> : HTMLInputElement, IFormControl<T>
{
    /// <summary>
    ///     Which control this is — text, checkbox, date, file, and so on. Choosing the right one gets you
    ///     the right mobile keyboard and the browser's own validation for free.
    /// </summary>
    public InputType? Type { get; set; }

    /// <summary>The name submitted with the form. A bound input defaults it to the bound member's name.</summary>
    public string? Name { get; set; }

    /// <summary>
    ///     The control's current value. Prefer <c>Bind</c>, which keeps it in step with your model in both
    ///     directions.
    /// </summary>
    public T? Value { get; set; }

    /// <summary>Whether a checkbox or radio starts checked.</summary>
    /// <remarks>
    ///     Controlled mode only — it is the checkbox's value. A bound control derives the checked state
    ///     from the model, so this is neither a step on a bound chain nor a parameter of the bound factory.
    /// </remarks>
    public bool? Checked { get; set; }

    /// <summary>
    ///     The granularity the value must snap to; <c>any</c> removes the restriction. A fractional bound type
    ///     defaults to <c>any</c>.
    /// </summary>
    public string? Step { get; set; }

    /// <summary>
    ///     Called on every keystroke with the raw text of the field, before any parsing — so it is the
    ///     hook for live search and character counters. Unlike <see cref="OnChange" /> it is a
    ///     <see langword="string" /> whatever <typeparamref name="T" /> is, and it fires while the value
    ///     is still half-typed and possibly not valid.
    /// </summary>
    /// <remarks>
    ///     Controlled mode only: a bound control installs its own <c>oninput</c> write-back and never
    ///     reads this. Use <see cref="AfterBind" /> for a side effect on each bound write.
    /// </remarks>
    public Callback<string> OnInput { get; set; }

    /// <summary>
    ///     Called with the parsed value once the user commits a change, in controlled mode. Store it and
    ///     pass it back through <c>Value</c>.
    /// </summary>
    public Callback<T> OnChange { get; set; }

    /// <summary>
    ///     Called with the chosen files when this is a file input. The list is empty when the user cancels
    ///     the picker, so check it before reading the first entry.
    ///     <para>
    ///         Never trust what arrives: a file's reported name, size and type all come from the client.
    ///         Re-check them on the server before storing anything.
    ///     </para>
    /// </summary>
    public Callback<IReadOnlyList<RaskFileType>> OnFiles { get; set; }

    /// <summary>
    ///     The model field this control is bound to, as an expression such as <c>() => model.Email</c>.
    ///     Rask reads the value, writes edits back, and infers the parse and the validation from the
    ///     field's type.
    /// </summary>
    public Expression<Func<T>>? Bind { get; set; }

    /// <summary>A check run on the bound value — synchronous, or asynchronous for a uniqueness lookup.</summary>
    public Validator<T>? Validate { get; set; }

    /// <summary>Runs after a successful bind, once the model has the new value.</summary>
    public Callback<T> AfterBind { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

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

        // A RADIO bound over a bool is the same control as a checkbox as far as the model is concerned:
        // it asks whether THIS option is the chosen one, and its state is `checked`. Without this it fell
        // through to the value branch and rendered `value="True"` with no checked at all. A radio bound over
        // anything else is carrying the group's value and still writes it.
        var isCheckbox = resolvedType == "checkbox"
                         || (resolvedType == "radio" && typeof(T) == typeof(bool));
        var name = Name ?? acc?.PropertyName;

        // Value / checked state. Bound mode derives one from the model (checkbox → checked, else → value);
        // plain/controlled mode honors the explicit Value/Checked props independently.
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

        if (checkedState is true)
        {
            AppendAttr(sb, "checked", null);
        }

        // An explicit Step always wins. Otherwise a fractional bound type needs step="any": HTML defaults
        // to step="1", so the browser's own constraint validation rejects 42.50 and never fires submit —
        // silently, with no validation message and nothing thrown. Same hazard on a range input.
        var step = Step ?? (resolvedType is "number" or "range" ? BindingHelpers.DefaultStep(typeof(T)) : null);
        if (step is not null)
        {
            AppendAttr(sb, "step", step);
        }

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        if (acc is not null)
        {
            // Bound: write the model on input (immediate for string) / change, validate.
            var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind);
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
            var input = OnInput.Handler;
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

        var files = OnFiles.Handler;
        if (files is not null)
        {
            AppendAttr(sb, "data-rask-on-files", ctx.RegisterHandler(files));
        }
    }
}
