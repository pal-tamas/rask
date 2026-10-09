using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;
using RaskFileType = Rask.Core.Forms.IRaskFile;

namespace Rask.Core;

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
    // A bound control writes back and re-renders through its own handle, unlike a plain tag.
    private protected override bool OwnsRenderHandle => true;

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

    /// <summary>
    ///     Writes the model as the reader types, after a 150 ms pause, and validates then:
    ///     <c>Input.Bind(() =&gt; m.Title).Live()</c>. Without it a bound field says nothing until the next
    ///     action — a press on a button, a submit, Enter — and its value travels with that.
    /// </summary>
    /// <remarks>
    ///     Bound mode. On a control that is chosen rather than typed into — a checkbox, a radio, a range, a
    ///     colour — it writes the model as the choice is made, with no pause. <see cref="Debounce" /> names a
    ///     pause of your own for a field that is typed into.
    /// </remarks>
    public bool? Live
    {
        get => _timing.Live;
        set => _timing.Live = value;
    }

    /// <summary>
    ///     Binds when the reader leaves the field, and validates then:
    ///     <c>Input.Bind(() =&gt; m.Name).Blur()</c>. Nothing is sent while they type.
    /// </summary>
    /// <remarks>
    ///     Bound mode, and a field that is typed into: a checkbox, a radio, a file or a range commits as it
    ///     is chosen.
    /// </remarks>
    public bool? Blur
    {
        get => _timing.Blur;
        set => _timing.Blur = value;
    }

    /// <summary>
    ///     Binds as the reader types, once typing has paused for this long, and validates then:
    ///     <c>Input.Bind(() =&gt; m.Name).Debounce(300.Milliseconds)</c>. The pause is kept in the browser, so
    ///     the keystrokes before it cost nothing. <see cref="TimeSpan.Zero" /> sends every keystroke.
    /// </summary>
    /// <remarks>
    ///     Bound mode, and a field that is typed into: a checkbox, a radio, a file or a range commits as it
    ///     is chosen.
    /// </remarks>
    public TimeSpan? Debounce
    {
        get => _timing.Debounce;
        set => _timing.Debounce = value;
    }

    private BindTiming _timing;
    private bool _saidInvalid;


    protected override void WriteAttributes(StringBuilder sb)
    {
        var acc = Bind is not null ? ExpressionAccessor.Parse(Bind) : null;
        var bindCtx = acc is not null ? BindingHelpers.ResolveBindingContext(acc.Target) : null;
        if (acc is not null)
        {
            SayInvalid(bindCtx, acc.Field, ref _saidInvalid);
        }

        base.WriteAttributes(sb);

        var resolvedType = ResolveType(acc);

        // A RADIO bound over a bool is the same control as a checkbox as far as the model is concerned:
        // it asks whether THIS option is the chosen one, and its state is `checked`. Without this it fell
        // through to the value branch and rendered `value="True"` with no checked at all. A radio bound over
        // anything else is carrying the group's value and still writes it.
        var isCheckbox = string.Equals(resolvedType, "checkbox", StringComparison.Ordinal)
                         || (string.Equals(resolvedType, "radio", StringComparison.Ordinal) && typeof(T) == typeof(bool));
        WriteValueAttributes(sb, acc, resolvedType, isCheckbox);

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        if (acc is not null)
        {
            WireBound(sb, ctx, acc, bindCtx, isCheckbox, BindingHelpers.IsTypedInto(resolvedType));
        }
        else
        {
            WirePlain(sb, ctx);
        }

        var files = OnFiles.Handler;
        if (files is not null)
        {
            AppendAttr(sb, "data-rask-on-files", ctx.RegisterHandler(files));
        }
    }

    // An explicit InputType wins; otherwise bound mode (and non-string T) default from T, while a plain
    // string input keeps "no type unless set".
    private string? ResolveType(ExpressionAccessor.Accessor? acc)
    {
        var resolvedType = Type?.ToHtml();
        if (resolvedType is null && (acc is not null || typeof(T) != typeof(string)))
        {
            resolvedType = BindingHelpers.DefaultInputType(typeof(T));
        }

        return resolvedType;
    }

    // Writes the five attributes this layer owns. Bound mode derives the value or the checked state from the
    // model, and plain or controlled mode honors the explicit Value and Checked props independently.
    private void WriteValueAttributes(StringBuilder sb, ExpressionAccessor.Accessor? acc, string? resolvedType, bool isCheckbox)
    {
        string? valueString = null;
        bool? checkedState = null;
        if (acc is null)
        {
            checkedState = Checked;
            valueString = Value is not null ? BindingHelpers.FormatValue(Value) : null;
        }
        else if (isCheckbox)
        {
            checkedState = acc.Getter() is true;
        }
        else
        {
            valueString = BindingHelpers.FormatValue(acc.Getter());
        }

        if (resolvedType is not null)
        {
            AppendAttr(sb, "type", resolvedType);
        }

        if ((Name ?? acc?.PropertyName) is { } name)
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
    }

    // Bound: a checkbox writes the model as it is ticked, a typed field when its timing says, the rest as chosen.
    private void WireBound(
        StringBuilder sb, LiveRenderContext ctx, ExpressionAccessor.Accessor acc, EditContext? bindCtx,
        bool isCheckbox, bool typedInto)
    {
        var fid = acc.Field;
        var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind);
        ((IFormControl<T>)this).RegisterValidator(acc, bindCtx);
        if (isCheckbox)
        {
            WriteChosenBind(sb, ctx, !_timing.WaitsForAction, BindingHelpers.BoolSetHandler(acc, bindCtx, fid, afterBind), bindCtx, fid);
            return;
        }

        if (typedInto)
        {
            WriteTypedBind(sb, ctx, _timing, acc, bindCtx, afterBind, BindingHelpers.IsImmediateUpdateType(typeof(T)));
            return;
        }

        // A radio carrying the group's value, a range, a colour: chosen, not typed. Sent as it is dragged or
        // picked only when the chain says `.Live()` and the model holds the text of it.
        if (!_timing.WaitsForAction && BindingHelpers.IsImmediateUpdateType(typeof(T)))
        {
            WriteKeystrokeBind(sb, ctx, acc, bindCtx, afterBind);
            return;
        }

        WriteChosenBind(sb, ctx, !_timing.WaitsForAction,
            BindingHelpers.TouchAndValidateHandler(acc, bindCtx, fid, true, afterBind), bindCtx, fid);
    }

    // Plain / controlled.
    private void WirePlain(StringBuilder sb, LiveRenderContext ctx)
    {
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
}
