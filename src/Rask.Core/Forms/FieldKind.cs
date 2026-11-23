namespace Rask.Core.Forms;

/// <summary>
///     What a field of a model holds, as a control is chosen for it: the half of a <see cref="FieldShape" /> the
///     property's own type cannot say — that a <c>string</c> is a short text, a <c>long</c> a whole number with bounds,
///     a <c>Guid</c> another row's id.
/// </summary>
public enum FieldKind
{
    /// <summary>Text, with the most and the fewest characters it may hold.</summary>
    Text,

    /// <summary>A number with a fraction, with its bounds and its scale.</summary>
    Number,

    /// <summary>A whole number, with its bounds.</summary>
    Whole,

    /// <summary>A day of the calendar, and whether it may be after today.</summary>
    Day,

    /// <summary>
    ///     A start and an end, held by the members <see cref="FieldShape.Start" /> and <see cref="FieldShape.End" />
    ///     name, each of the kind <see cref="FieldShape.Ends" /> says.
    /// </summary>
    Range,

    /// <summary>The id of another row: a choice among rows, never a value typed in.</summary>
    Id,

    /// <summary>One of a fixed set — an enum's members, listed with their labels in <see cref="FieldShape.Options" />.</summary>
    Choice,

    /// <summary>A moment on the wall clock — a day and a time of day — and whether it may be after now.</summary>
    Moment,
}
