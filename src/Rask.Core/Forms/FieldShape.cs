namespace Rask.Core.Forms;

/// <summary>
///     What a field of a model is, beyond its property's type: the rules its value object declares (<c>Text(Value,
///     MaxLength: 100)</c>, <c>Range(Start, End, Strict: true)</c>), told to the controls bound to it — a text input
///     is given its <c>maxlength</c> and <c>minlength</c> — instead of found out on submit. Registered per model type
///     through <see cref="RaskValidation.RegisterFieldShapes" />, which a generated model does for every field it holds.
/// </summary>
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
    IReadOnlyList<FieldOption>? Options = null);
