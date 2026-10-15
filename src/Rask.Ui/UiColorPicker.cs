using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:color-picker</c>: a colour picked from a palette, dragged out of a saturation and
///     brightness area, or typed.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.ColorPicker.Bind(() =&gt; settings.Theme)</c> two-way binds a string member;
///     <c>Ui.ColorPicker.Value(theme).OnChange(c =&gt; theme = c)</c> leaves the colour with the page; with
///     neither <c>Bind</c> nor <c>OnChange</c> the picker keeps the colour itself, starting from
///     <see cref="Value" />. The value is CSS text in <see cref="Format" />, whatever CSS it was given in.
///     </para>
///     <para>
///     The popover is a native modal <c>&lt;dialog&gt;</c> that the trigger's button opens by command, so
///     the top layer and the focus coming back are the browser's. Below <c>sm</c> it is a sheet over the page
///     that holds what is chosen until its Select button is pressed, as Flux's does. Children replace its
///     layout: compose <c>Ui.ColorPickerArea</c>, <c>Ui.ColorPickerSlider</c>, <c>Ui.ColorPickerInput</c>,
///     <c>Ui.ColorPickerSwatches</c> and <c>Ui.ColorPickerDropper</c> in any order.
///     </para>
///     <para>
///     The colour maths and the keys are C#; the pointer is the runtime's (<c>data-rask-drag</c>), which
///     keeps a thumb under it every frame and tells the page through one bound field a surface.
///     </para>
/// </remarks>
public sealed partial class UiColorPicker : Component, IFormControl<string>, IUiFormControl, IDeferredBind
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["ui-control"] = string.Empty,
        ["ui-color-picker"] = string.Empty,
    };

    private static readonly Dictionary<string, string?> Announced = new(StringComparer.Ordinal)
    {
        ["live"] = "polite",
        ["atomic"] = "true",
    };

    private readonly int _instance = UiInstanceCounter.Next();

    private string? _ownId;

    /// <summary>The colour, as any CSS colour string. The first colour of a picker that keeps its own.</summary>
    public string? Value { get; set; }

    /// <summary>Called with the new colour, in <see cref="Format" />, when the page holds it.</summary>
    public Callback<string> OnChange { get; set; }

    /// <summary>The member the picker reads and writes: Flux's <c>wire:model</c>.</summary>
    public Expression<Func<string>>? Bind { get; set; }

    /// <summary>A rule for this field, run with the form's own.</summary>
    public Validator<string>? Validate { get; set; }

    /// <summary>Called after a colour has been written to the bound member and validated.</summary>
    public Callback<string> AfterBind { get; set; }

    /// <summary>
    ///     Writes the model as soon as a colour is chosen, and validates then:
    ///     <c>Ui.ColorPicker.Bind(() =&gt; m.Theme).Live()</c>. Flux's <c>wire:model.live</c>. Without it the
    ///     colour is written with the next action, and travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live { get; set; }

    /// <summary>Adds a hidden input of this name, so a plain HTML form posts the colour.</summary>
    public string? Name { get; set; }

    /// <summary>What the colour is written as. An alpha format adds an alpha slider to the popover.</summary>
    public Ui.ColorPickerFormat? Format { get; set; }

    /// <summary>An input to type into with a swatch button, or one button showing the colour.</summary>
    public Ui.ColorPickerType? Type { get; set; }

    /// <summary>Shown while there is no colour. By format when unset: <c>#000000</c> for hex.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall the trigger is.</summary>
    public Ui.ColorPickerSize? Size { get; set; }

    /// <summary>The palette, in place of the 24 Tailwind colours. An empty list shows none: Flux's <c>false</c>.</summary>
    public IReadOnlyList<UiColorSwatch>? Swatches { get; set; }

    /// <summary>True for a button in the popover that picks a colour from anywhere on the screen.</summary>
    public bool? Dropper { get; set; }

    /// <summary>True for a button on the input trigger that clears the colour, shown while there is one.</summary>
    public bool? Clearable { get; set; }

    /// <summary>True for a button on the input trigger that copies the colour, as it is written, to the clipboard.</summary>
    public bool? Copyable { get; set; }

    /// <summary>Flux's <c>label</c>: wraps the picker in a field with this label over it.</summary>
    public string? Label { get; set; }

    /// <summary>Flux's <c>description</c>: help text between the label and the picker.</summary>
    public string? Description { get; set; }

    /// <summary>True for a picker that cannot be opened or changed.</summary>
    public bool? Disabled { get; set; }

    /// <summary>True for error styling the form did not ask for. A bound picker is invalid while its member holds a message.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Classes for the call site, added to the picker's own.</summary>
    public string? Class { get; set; }

    /// <summary>The trigger's id. Unset, it is derived from the bound member or the label.</summary>
    public string? Id { get; set; }

    /// <inheritdoc />
    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    /// <inheritdoc />
    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    string? IUiFormControl.DescriptionTrailing => null;

    /// <inheritdoc />
    string? IUiFormControl.Badge => null;

    /// <inheritdoc />
    bool? IUiFormControl.ShowValidation => null;

    // The colour held is a FIELD, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private string Prefixed => "uicp-" + _instance.ToString(CultureInfo.InvariantCulture);

    private string DialogId => Prefixed + "-popover";

    private Ui.ColorPickerFormat Written => Format ?? Ui.ColorPickerFormat.Hex;

    private Ui.ColorPickerSize Height => Size ?? Ui.ColorPickerSize.Base;

    private bool HasAlpha => Written is Ui.ColorPickerFormat.Hexa or Ui.ColorPickerFormat.Rgba or Ui.ColorPickerFormat.Hsla;

    private bool Off => Disabled == true;

    // Only the hex placeholder is on Flux's page; its one other live format, rgba, says "Enter a color".
    private string Hint => Placeholder
        ?? (Written == Ui.ColorPickerFormat.Hex ? "#000000" : RaskStrings.Get(RaskString.ColorPickerPlaceholder, "Enter a color"));

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        Read();

        return field.Wrap(
            Div.Class(UiClass.Compose(UiColorPickerLook.Root, Class))
                .Style("--ui-color-picker-anchor:--" + Prefixed)
                .Data(Off ? Marks : Hooked())[
                ValueField(),
                Type == Ui.ColorPickerType.Button ? ButtonTrigger(field) : InputTrigger(field),
                Span.Class(UiColorPickerLook.Announce).Data(UiColorPickerMarks.Announced).Aria(Announced),
                Popover(field.LabelledBy)
            ]);
    }

    // What the browser keeps the picker by: its format, the words it writes, and whether text that is no colour is
    // the page's to refuse. `data-open` is the browser's too, while the dialog shows.
    private Dictionary<string, string?> Hooked()
    {
        var marks = new Dictionary<string, string?>(Marks, StringComparer.Ordinal)
        {
            ["rask-color"] = UiColorPickerMarks.FormatName(Written),
            ["rask-color-says"] = UiColorPickerMarks.Says(),
            ["rask-open-mark"] = DialogId,
        };
        if (Refuses)
        {
            marks["rask-color-refuses"] = string.Empty;
        }

        return marks;
    }

    // The one field the page hears the colour through, first in the picker: what the browser announces when Flux's
    // element would. On change, or as it moves for a bound picker that says `.Live()`; bound and waiting, it goes
    // ahead of the reader's next action. A picker that keeps its own colour hears nothing.
    private HTMLInputElement<string> ValueField()
    {
        var field = Input.Value(_refused ?? _kept.Text(Written)).Type(InputType.Hidden);
        if (Name is { } name)
        {
            field = field.Name(name);
        }

        if (Keeps || Off)
        {
            return field;
        }

        if (Bind is not null && Live == true)
        {
            return field.OnInput(ChangedAsync);
        }

        field = field.OnChange(ChangedAsync);
        return UiDraws.Waits(this, Bind is not null) ? field.Attributes(UiColorPickerMarks.WaitsForAction) : field;
    }
}
