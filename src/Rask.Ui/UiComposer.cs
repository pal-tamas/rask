using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux UI's composer: a message box with buttons around it, for a chat or a prompt.
/// </summary>
/// <remarks>
///     <code>
///     Form.Model(chat).OnSubmit(Send)[
///         Ui.Composer.Bind(() =&gt; chat.Prompt).Label("Prompt").LabelSrOnly().Placeholder("How can I help you today?")
///             .ActionsLeading(Ui.Button.Sm.Subtle.Icon(Ui.IconName.PaperClip))
///             .ActionsTrailing(Ui.Button.Type(Ui.ButtonType.Submit).Sm.Primary.Icon(Ui.IconName.PaperAirplane))
///     ]
///     </code>
///     <para>
///     Ctrl+Enter or Cmd+Enter submits the form it is in (<see cref="Submit" />: Enter alone), in the browser, and
///     what was typed travels with that submit; nothing else is sent while the reader types. The box grows with the
///     text from <see cref="Rows" /> lines to <see cref="MaxRows" />, then scrolls. <see cref="Input" /> puts a
///     <c>Ui.Editor</c> where the box is, drawn without its border and bound to the composer's value.
///     </para>
/// </remarks>
public sealed partial class UiComposer<T> : Component, IFormControl<T>, IUiFormControl
{
    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <summary>Keeps the label for screen readers only.</summary>
    public bool? LabelSrOnly { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <summary>Keeps the description for screen readers only.</summary>
    public bool? DescriptionSrOnly { get; set; }

    /// <summary>Shown while the box is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How many lines the box shows to start with. Two unless set.</summary>
    public int? Rows { get; set; }

    /// <summary>How many lines the box grows to before it scrolls. Ten unless set.</summary>
    public int? MaxRows { get; set; }

    /// <summary>Puts the buttons in one row with the box, the leading ones before it.</summary>
    public bool? Inline { get; set; }

    /// <summary>Which key submits the form: Ctrl/Cmd + Enter unless set.</summary>
    public Ui.ComposerSubmit? Submit { get; set; }

    /// <summary>The corners: a message box's unless set, or those of the other inputs.</summary>
    public Ui.ComposerVariant? Variant { get; set; }

    /// <summary>Locks the box and every button in it.</summary>
    public bool? Disabled { get; set; }

    /// <inheritdoc cref="UiInput{T}.Invalid" />
    public bool? Invalid { get; set; }

    /// <summary>The name the text posts under from a plain HTML form.</summary>
    public string? Name { get; set; }

    /// <summary>What stands where the box is — a <c>Ui.Editor</c> for rich text.</summary>
    /// <remarks>Hides the inherited <c>Input</c> tag entry inside this component; <c>Markup.Input</c> still reaches the tag.</remarks>
    public new Component? Input { get; set; }

    /// <summary>What sits above the box: the files about to be sent, say.</summary>
    /// <remarks>Hides the inherited <c>Header</c> tag entry inside this component; <c>Markup.Header</c> still reaches the tag.</remarks>
    public new Component? Header { get; set; }

    /// <summary>What sits under the buttons.</summary>
    /// <remarks>Hides the inherited <c>Footer</c> tag entry inside this component; <c>Markup.Footer</c> still reaches the tag.</remarks>
    public new Component? Footer { get; set; }

    /// <summary>The buttons at the start of the row under the box (before it, <see cref="Inline" />).</summary>
    public Component? ActionsLeading { get; set; }

    /// <summary>The buttons at the end, the submit button among them.</summary>
    public Component? ActionsTrailing { get; set; }

    /// <summary>Classes for the composer's box.</summary>
    public string? Class { get; set; }

    /// <summary>The id of the <c>&lt;textarea&gt;</c>; derived from the binding or the label when unset.</summary>
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T> AfterBind { get; set; }

    /// <summary>
    ///     Writes the model as the reader types, after a 150 ms pause, and validates then:
    ///     <c>Ui.Composer.Bind(() =&gt; m.Prompt).Live()</c>. Flux's <c>wire:model.live</c>. Without it the text
    ///     travels with the submit — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live
    {
        get => _timing.Live;
        set => _timing.Live = value;
    }

    /// <summary>
    ///     Binds as the reader types, once typing has paused for this long, and validates then:
    ///     <c>Ui.Composer.Bind(() =&gt; m.Prompt).Debounce(300.Milliseconds)</c>. Flux's
    ///     <c>wire:model.live.debounce.300ms</c>.
    /// </summary>
    public TimeSpan? Debounce
    {
        get => _timing.Debounce;
        set => _timing.Debounce = value;
    }

    private BindTiming _timing;

    // Nothing names it — no Id, no bound member, no label: an id of its own.
    private string? _ownId;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => null;

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var inline = Inline == true;
        var rows = Math.Max(Rows ?? 2, 1);
        var maxRows = Math.Max(MaxRows ?? 10, rows);
        var disabled = Disabled == true;
        if (disabled)
        {
            UiComposerLook.Disable(ActionsLeading);
            UiComposerLook.Disable(ActionsTrailing);
        }

        var box = Input is UiEditor editor
            ? editor.HostedBy(new UiEditorHost(EditorBind(), EditorValue(), EditorChange(), Placeholder, rows, maxRows, disabled))
            : Input ?? Text(field.ControlId, rows, maxRows, disabled);
        var buttons = UiClass.Compose(
            Variant == Ui.ComposerVariant.Input ? null : UiComposerLook.RoundButtons,
            disabled ? UiComposerLook.LockedButtons : null);

        var root = Div.Class(UiClass.Compose(
                UiComposerLook.Root,
                Variant == Ui.ComposerVariant.Input ? UiComposerLook.InputCorners : UiComposerLook.Corners,
                Class))
            .Role("group")
            .Aria(Names(field))
            .Data(UiComposerLook.Marks(Submit ?? Ui.ComposerSubmit.CmdEnter, field.Invalid));

        root = disabled ? root.Attributes(UiComposerLook.Locked) : root;

        return field.Wrap(
            root[
                Div.Class(Header is null ? UiComposerLook.NoHeader : UiComposerLook.Header)[Header],
                Div.Class(inline ? UiComposerLook.InlineBox : UiComposerLook.Box)[box],
                Div.Class(UiClass.Compose(inline ? UiComposerLook.InlineLeading : UiComposerLook.Leading, buttons))[ActionsLeading],
                Div.Class(UiClass.Compose(inline ? UiComposerLook.InlineTrailing : UiComposerLook.Trailing, buttons))[ActionsTrailing],
                Div.Class(Footer is null ? UiComposerLook.NoFooter : UiComposerLook.Footer)[Footer]
            ],
            labelSrOnly: LabelSrOnly == true,
            descriptionSrOnly: DescriptionSrOnly == true);
    }

    // The <textarea>: Core's, bound or given its value; `rows` lines tall, grown by the runtime to `maxRows`.
    private HTMLTextAreaElement<T> Text(string id, int rows, int maxRows, bool disabled)
    {
        // Bind and Value are the two openings of Core's textarea, and both hand back the same element.
        var textarea = Bind is { } bind
            ? Textarea.Bind(bind).Validate(Validate).AfterBind(AfterBind).Live(Live).Debounce(Debounce)
            : Textarea.Value(Value).OnChange(OnChange);

        return textarea
            .Id(id)
            .Name(Name)
            .Placeholder(Placeholder)
            .Rows(rows)
            .Disabled(disabled)
            .Data("rask-grow", maxRows.ToString(CultureInfo.InvariantCulture))
            .Class(UiComposerLook.Text);
    }

    // Flux names the group by its label and describes it by its description — not by the error under it.
    private Dictionary<string, string?> Names(UiWithField field)
    {
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (field.LabelledBy is { } label)
        {
            names["labelledby"] = label;
        }

        if (Description is not null)
        {
            names["describedby"] = UiFieldId.Description(((IUiFieldControl)this).ControlId);
        }

        return names;
    }

    // The editor's value is a string: a composer of strings hands it its binding, any other type its own.
    private Expression<Func<string?>>? EditorBind() => (object?)Bind as Expression<Func<string?>>;

    private string? EditorValue() => Value as string;

    private Callback<string> EditorChange() => (object)OnChange is Callback<string> heard ? heard : default;
}
