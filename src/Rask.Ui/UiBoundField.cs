using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     Flux UI's field drawn from a bound member: <c>Ui.Field.Bind(() =&gt; model.Name)</c> — the label, the help text, the
///     control the member's shape calls for and its message, with nothing else said.
/// </summary>
/// <remarks>
///     <para>
///         The control follows what the member holds — its <see cref="FieldShape" />, which a generated model registers,
///         else its type: text is a <c>Ui.Input</c> (<c>email</c>, <c>tel</c>, <c>url</c> or <c>password</c> where the member
///         says so), <c>.Multiline()</c> a <c>Ui.Textarea</c>; a number an input with its bounds and its step; a day a
///         <c>Ui.DatePicker</c>, a moment a <c>Ui.DayAndTime</c>; a <c>bool</c> a <c>Ui.Checkbox</c>; an enum a
///         <c>Ui.Select</c> over its members; another row's id a <c>Ui.Select</c> over the <c>.Choices(…)</c> you give;
///         a range two controls under one label, each end with its own message.
///     </para>
///     <para>
///         The label is the member's <c>[Display(Name)]</c>, the help text its <c>[Display(Description)]</c>, the
///         placeholder its <c>[Display(Prompt)]</c>; each step of the same name wins. <c>[Editable(false)]</c> makes a text
///         or number input read-only and every other control disabled. A <c>DateTime</c> its shape says is a day
///         (<c>[DataType(DataType.Date)]</c>, a <see cref="FieldKind.Day" /> shape) is a <c>Ui.DatePicker</c> alone, a day
///         picked keeping the time of day the value had; <c>[DataType(DataType.MultilineText)]</c> a textarea, and <c>[UIHint]</c> picks
///         the kit control of that name where it fits (<see cref="FieldHints" />): a <c>Ui.Switch</c> for a <c>bool</c>, a
///         <c>Ui.RadioGroup</c> for an enum. <c>Ui.Field[…]</c> with parts of your own is still the field you compose by hand.
///     </para>
/// </remarks>
/// <typeparam name="T">The member's type.</typeparam>
[RaskChainEntry("UiField")]
public sealed partial class UiBoundField<T> : Component
{
    /// <summary>The member the field reads and writes: <c>() =&gt; model.Name</c>.</summary>
    public required Expression<Func<T>> Bind { get; set; }

    /// <summary>The label, in place of the member's <c>[Display(Name)]</c>.</summary>
    public string? Label { get; set; }

    /// <summary>The help text under the label, in place of the member's <c>[Display(Description)]</c>.</summary>
    public string? Description { get; set; }

    /// <summary>The placeholder, in place of the member's <c>[Display(Prompt)]</c>. Not every control shows one.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Flux's <c>badge</c>, beside the label, in place of the automatic <em>Required</em>; <c>""</c> says nothing.</summary>
    public string? Badge { get; set; }

    /// <summary>
    ///     Text drawn as a <c>Ui.Textarea</c> rather than a <c>Ui.Input</c>. Unset, a member that is
    ///     <c>[DataType(DataType.MultilineText)]</c> is drawn as one; false keeps it an input.
    /// </summary>
    public bool? Multiline { get; set; }

    /// <summary>False: the message under the control is left to a <c>Ui.Error</c> of your own.</summary>
    public bool? ShowValidation { get; set; }

    /// <summary>The control, or both of a range's, disabled.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Writes the model as the reader types or picks: the control's own <c>Live</c>.</summary>
    public bool? Live { get; set; }

    /// <summary>Writes the model when the reader leaves the field. A text field's only.</summary>
    public bool? Blur { get; set; }

    /// <summary>Writes the model once typing has paused this long. A text field's only.</summary>
    public TimeSpan? Debounce { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var bound = ExpressionAccessor.Parse(Bind);

        // A field the work in flight may not write (a gated member, its gate shut) is not drawn at all.
        if (RaskValidation.IsHidden(bound))
        {
            return null;
        }

        var shape = RaskValidation.ShapeOf(bound);
        var field = shape is { Kind: FieldKind.Range }
            ? UiBoundRange.Draw(this, Bind, shape, Look(shape))
            : UiBoundControl.Draw(Bind, shape, Look(shape), _choices);

        // A value that may be taken away rather than kept (an operation's Change<T>): a Clear beside it.
        return RaskValidation.ClearedByOf(bound) is { } clearedBy
            ? Div.Class("flex flex-col gap-2")[
                field,
                Ui.Checkbox.Bind(UiClearedBy.Of(bound.Target, clearedBy)).Label(RaskStrings.Get(RaskString.ChangeClear, "Clear"))
            ]
            : field;
    }

    private UiBoundLook Look(FieldShape? shape) =>
        new(
            Label ?? DisplayNames.Of(Bind),
            Description ?? DisplayNames.DescriptionOf(Bind),
            Placeholder ?? DisplayNames.PromptOf(Bind),
            Badge,
            ShowValidation,
            Disabled == true,
            shape?.ReadOnly == true,
            Multiline ?? string.Equals(shape?.Hint, FieldHints.MultilineText, StringComparison.Ordinal),
            new UiBoundTiming(Live, Blur, Debounce));
}
