using System.Linq.Expressions;
using Rask.Core.Components;

namespace Rask;

/// <summary>
/// Flux UI's radio: one choice of a <see cref="UiRadioGroup{T}" /> — <c>Ui.Radio.Value(Plan.Pro).Label("Pro")</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Value</c> is what the group's bound member becomes when this one is chosen. The group is what is
/// bound; a radio holds no state of its own.
/// </para>
/// <para>
/// The root (<c>data-ui-radio</c>) is a <c>&lt;label&gt;</c> holding a real <c>&lt;input type="radio"&gt;</c>
/// that shares the group's <c>name</c>, so the arrow keys move and choose, Tab enters the group at the chosen
/// radio, and a form posts the value — all the browser's. The group's variant draws it as a dot with its
/// label, a segment, a card, a pill or a button; in a card the children replace the label and description,
/// with a <see cref="UiRadioIndicator" /> wherever you place it.
/// </para>
/// </remarks>
public sealed partial class UiRadio : Component, IUiFormControl
{
    private UiRadioScope? _group;
    private string? _ownId;

    /// <summary>The words beside the dot — or on the segment, card, pill or button a group draws it as.</summary>
    public string? Label { get; set; }

    /// <summary>Help text under the label.</summary>
    public string? Description { get; set; }

    /// <summary>What the group's value becomes when this is chosen. Also the value a plain form posts.</summary>
    public object? Value { get; set; }

    /// <summary>Chosen from the start, while the group itself holds no value.</summary>
    public bool? Checked { get; set; }

    /// <summary>Cannot be chosen and is skipped by the arrow keys.</summary>
    public bool? Disabled { get; set; }

    /// <summary>A Heroicon before the label of a segment, a card or a button.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>
    ///     Attributes for the <c>&lt;input&gt;</c> itself, written as given — what Flux forwards to it:
    ///     <c>.Attributes(("aria-label", "Select row"))</c>, <c>("required", "")</c>. A <c>name</c> given here wins over
    ///     <see cref="Name" />.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Attributes { get; set; }

    /// <summary>
    ///     The <c>name</c> the radio posts under: the <c>&lt;input&gt;</c>'s own attribute. Unset, it is its group's,
    ///     which is what makes the radios one choice; set, it replaces that.
    /// </summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="Element.Id" />
    /// <remarks>On the <c>&lt;input&gt;</c>, which is what a <c>&lt;label for&gt;</c> names and a test clicks.</remarks>
    public string? Id { get; set; }

    public string? Class { get; set; }

    string IUiFieldControl.ControlId =>
        Id ?? (_group is { } group ? UiFieldId.Choice(group.GroupId, Value) : Standalone());

    LambdaExpression? IUiFieldControl.Bound => null;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => null;

    bool? IUiFormControl.Invalid => null;

    bool? IUiFormControl.ShowValidation => null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        _group = Context.Get<UiRadioScope>();
        var variant = _group?.Variant ?? Ui.RadioGroupVariant.Default;

        // A segment, a card, a pill and a button draw their own words: there is no field around them.
        var field = variant == Ui.RadioGroupVariant.Default ? UiWithField.For(this) : UiWithField.For((IUiFieldControl)this);
        var on = _group is { Current: not null } group ? Equals(group.Current, Value) : Checked == true;
        var input = Native(field, on);

        return variant switch
        {
            Ui.RadioGroupVariant.Segmented => Mark(Segment(), "ui-radio-segmented", on)[
                input,
                Icon is { } icon ? Ui.Icon.Name(icon).Mini.Class(UiOptionLook.SegmentIcon) : null,
                Words()
            ],
            Ui.RadioGroupVariant.Cards => Mark(UiOptionLook.Card, "ui-radio-cards", on)[input, CardContent()],
            Ui.RadioGroupVariant.Pills => Mark(UiOptionLook.Pill, "ui-radio-pills", on)[input, Words()],
            Ui.RadioGroupVariant.Buttons => Mark(UiOptionLook.Button, "ui-radio-buttons", on)[
                input,
                Icon is { } icon ? Ui.Icon.Name(icon).Micro.Class(UiOptionLook.ButtonIcon) : null,
                Span[Words()]
            ],
            _ => field.Wrap(
                Mark(UiOptionLook.Radio, "ui-radio", on)[input, Ui.RadioIndicator],
                Ui.FieldVariant.Inline,
                controlFirst: true),
        };
    }

    // Outside a group: named by its label, or — with none — by an id nothing else on the page has.
    private string Standalone() => Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(null, null, Label);

    private string Segment() =>
        UiClass.Compose(UiOptionLook.Segment, UiOptionLook.SegmentSize(_group?.Size ?? Ui.RadioGroupSize.Base));

    // RaskMarkup.Label, qualified: this type's Label property hides the chain entry of the same name.
    private HTMLLabelElement Mark(string look, string marker, bool on)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-" + UiOptionLook.ControlMark] = "",
            ["data-" + marker] = "",
        };

        // Flux marks a card, a pill and a button as the field they are: label and control in one.
        if (marker is "ui-radio-cards" or "ui-radio-pills" or "ui-radio-buttons")
        {
            marks["data-ui-field"] = "";
        }

        if (on)
        {
            marks["data-checked"] = "";
        }

        if (_group?.Invalid == true)
        {
            marks["data-invalid"] = "";
        }

        return RaskMarkup.Label.Class(UiClass.Compose(look, Class)).Attributes(marks);
    }

    // The change event of a radio only ever says "chosen": the value to commit is this radio's own.
    private HTMLInputElement<string> Native(UiWithField field, bool on) =>
        Input.Value(UiOptionLook.Posted(Value))
            .Type(InputType.Radio)
            .Id(field.ControlId)
            .Name(Attributes?.ContainsKey("name") == true ? null : Name ?? _group?.Name)
            .Checked(on)
            .Disabled(Disabled == true)
            .OnChange(_ => _group?.Choose(Value) ?? Task.CompletedTask)
            .Aria(field.Aria)
            .Attributes(UiOptionLook.Marks(Attributes))
            .Class(UiOptionLook.Native);

    private object? Words() => Children?.Any() == true ? Children : Label;

    private IEnumerable<Component?> CardContent()
    {
        if (Children?.Any() == true)
        {
            return Children;
        }

        return
        [
            Div.Class(UiOptionLook.CardBody)[
                Icon is { } icon ? Ui.Icon.Name(icon).Micro.Class(UiOptionLook.CardIcon) : null,
                Div.Class("flex-1")[
                    Div.Class(UiOptionLook.CardHeading).Data("ui-heading", "")[Label],
                    Description is null ? null : Div.Class(UiOptionLook.CardSubheading).Data("ui-subheading", "")[Description]
                ]
            ],
            _group?.Indicator == false ? null : Ui.RadioIndicator.Class(UiOptionLook.InCard),
        ];
    }
}
