namespace Rask.Core.Forms;

/// <summary>
///     What a field of a model is, beyond its property's type: the rules its value object declares (<c>Text(Value,
///     MaxLength: 100)</c>, <c>Range(Start, End, Strict: true)</c>), told to the controls bound to it — a text input
///     is given its <c>maxlength</c> and <c>minlength</c> — instead of found out on submit. Registered per model type
///     through <see cref="RaskValidation.RegisterFieldShapes" />, which a generated model does for every field it holds;
///     a member of a model that registered nothing is read off its DataAnnotations attributes instead.
/// </summary>
/// <remarks>
///     Beyond the rules, the hints the member's attributes give a control — <see cref="Input" />, <see cref="ReadOnly" />,
///     <see cref="KeepEmpty" />, <see cref="Hint" /> — and the way a value object keeps its text (<see cref="Upper" />).
/// </remarks>
/// <param name="Kind">What the field holds.</param>
/// <param name="MaxLength">The most characters a <see cref="FieldKind.Text" /> may hold, or null for no limit.</param>
/// <param name="MinLength">The fewest characters a <see cref="FieldKind.Text" /> may hold, or null for none.</param>
/// <param name="Min">The least a number, a whole number or the ends of a range may be, or null.</param>
/// <param name="Max">The greatest a number, a whole number or the ends of a range may be, or null.</param>
/// <param name="Scale">The most digits after the point a <see cref="FieldKind.Number" /> may have, or null.</param>
/// <param name="NotAfterToday">Whether a <see cref="FieldKind.Day" /> may not be after today.</param>
/// <param name="Strict">Whether a <see cref="FieldKind.Range" />'s end must be after its start, not only at least it.</param>
/// <param name="Start">The member that holds a range's start (<c>Start</c>, <c>From</c>), or null.</param>
/// <param name="End">The member that holds a range's end (<c>End</c>, <c>To</c>), or null.</param>
/// <param name="Ends">
///     The kind of a range's ends: <see cref="FieldKind.Whole" />, <see cref="FieldKind.Number" /> or
///     <see cref="FieldKind.Day" />; null for ends of any other type — a moment (<c>DateTime</c>), a time of day.
/// </param>
/// <param name="Options">The values a <see cref="FieldKind.Choice" /> may take, in declaration order, or null.</param>
/// <param name="NotAfterNow">Whether a <see cref="FieldKind.Moment" /> may not be after now.</param>
public sealed record FieldShape(
    FieldKind Kind,
    int? MaxLength = null,
    int? MinLength = null,
    decimal? Min = null,
    decimal? Max = null,
    int? Scale = null,
    bool NotAfterToday = false,
    bool Strict = false,
    string? Start = null,
    string? End = null,
    FieldKind? Ends = null,
    IReadOnlyList<FieldOption>? Options = null,
    bool NotAfterNow = false)
{
    /// <summary>
    ///     The input a text is typed into, where the member's attributes ask for one: <c>[EmailAddress]</c> an
    ///     <see cref="InputType.Email" />, <c>[Phone]</c> a <see cref="InputType.Tel" />, <c>[Url]</c> a
    ///     <see cref="InputType.Url" />, <c>[DataType(DataType.Password)]</c> a <see cref="InputType.Password" />,
    ///     <c>[DataType(DataType.Date)]</c> a <see cref="InputType.Date" /> — or its value object's kind of text, the same
    ///     way: <c>Email</c>, <c>Phone</c>, <c>Url</c>, <c>Password</c>. Null for the input the member's type calls for.
    /// </summary>
    public InputType? Input { get; init; }

    /// <summary>
    ///     What the browser may fill the input with — its <c>autocomplete</c>: <c>"email"</c> for a value object declared
    ///     from <c>Email</c>, <c>"tel"</c> from <c>Phone</c>, <c>"current-password"</c> or <c>"new-password"</c> from
    ///     <c>Password</c>. Null for none; an <c>Autocomplete</c> the author sets wins.
    /// </summary>
    public string? AutoComplete { get; init; }

    /// <summary>
    ///     The keyboard a touch screen shows for the input — its <c>inputmode</c>: <see cref="Core.InputMode.Tel" /> for a
    ///     value object declared from <c>Phone</c>. Null for the keyboard its type calls for; an <c>InputMode</c> the author sets wins.
    /// </summary>
    public InputMode? InputMode { get; init; }

    /// <summary>Whether the member is <c>[Editable(false)]</c>: shown, never changed by the reader.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    ///     Whether a text the reader empties is kept as <c>""</c> rather than written as <c>null</c>:
    ///     <c>[DisplayFormat(ConvertEmptyStringToNull = false)]</c>.
    /// </summary>
    public bool KeepEmpty { get; init; }

    /// <summary>
    ///     Whether the text is kept in capitals — a value object declared <c>Text(Value, Upper: true)</c> — so its input
    ///     shows what is typed in capitals as well.
    /// </summary>
    public bool Upper { get; init; }

    /// <summary>
    ///     The control the member names — its <c>[UIHint]</c>, or <c>"MultilineText"</c> for a value object declared from
    ///     <c>Multiline</c> and for
    ///     <c>[DataType(DataType.MultilineText)]</c> — or null. A bound field draws the kit control of that name where
    ///     one fits what the member holds (<see cref="FieldHints" />), and its usual control otherwise.
    /// </summary>
    public string? Hint { get; init; }
}
