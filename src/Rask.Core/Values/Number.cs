using System.Globalization;

namespace Rask.Values;

/// <summary>
///     The base of a value object that holds one decimal number:
///     <c>public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);</c>
/// </summary>
/// <remarks>
///     The constructor checks the bounds and the digits and throws a <see cref="Rask.Cqrs.RaskValidationException" />
///     when one refuses the number; the generated half is <see cref="Text" />'s, for a <see cref="decimal" />. The
///     column is given the declared <c>Precision</c> and <c>Scale</c>. Two values are equal when they are of the same
///     type and hold the same number.
/// </remarks>
public abstract record Number : Value
{
    /// <summary>Keeps <paramref name="Value" /> once the rules the other arguments declare accept it.</summary>
    /// <param name="Value">The number as given; the derived record's own positional parameter.</param>
    /// <param name="Min">The least it may be, or null.</param>
    /// <param name="Max">The greatest it may be, or null.</param>
    /// <param name="Scale">The most decimal places it may have, and the column's scale; or null.</param>
    /// <param name="Precision">The most digits it may have in all, and the column's precision; or null.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="Min" /> exceeds <paramref name="Max" />, or the precision and scale cannot describe a number.
    /// </exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the number.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Number(Value, Min: 1, Scale: 2)`
    protected Number(decimal Value, decimal? Min = null, decimal? Max = null, int? Scale = null, int? Precision = null)
#pragma warning restore IDE1006
        : base(checkNow: false)
    {
        if (Min > Max)
        {
            throw new ArgumentOutOfRangeException(nameof(Min), Min, "The least a number may be is above the greatest.");
        }

        if (Scale is < 0 or > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(Scale), Scale, "A scale is 0 to 28 decimal places.");
        }

        if (Precision is < 1 or > 38 || Scale > Precision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Precision), Precision, "A precision is 1 to 38 digits, and at least the scale.");
        }

        if (DeclaredRules.IsReading)
        {
            DeclaredRules.Note(new NumberRules(Min, Max, Scale, Precision));
        }

        this.Value = Value;
        if (!RowLoading.IsOn)
        {
            Check(Problems(Value, Min, Max, Scale, Precision));
        }
    }

    /// <summary>The number it holds.</summary>
    public decimal Value { get; }

    /// <summary>The number it holds, written culture-invariant: a value's text, not its display (that is the UI's).</summary>
    /// <returns>The number as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private static List<Problem>? Problems(decimal number, decimal? min, decimal? max, int? scale, int? precision)
    {
        List<Problem>? problems = null;

        if (number < min)
        {
            (problems ??= []).Add(Say(ValueProblemKind.BelowMin, min));
        }

        if (number > max)
        {
            (problems ??= []).Add(Say(ValueProblemKind.AboveMax, max));
        }

        if (scale is { } places && decimal.Round(number, places) != number)
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooManyDecimals, places));
        }

        if (precision is { } digits && !FitsDigits(number, digits - (scale ?? 0)))
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooManyDigits));
        }

        return problems;
    }

    private static Problem Say(ValueProblemKind kind, object? limit = null) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit)));

    // Whether the whole part has at most `digits` digits. 10^29 is past decimal's range, so beyond 28 anything fits.
    private static bool FitsDigits(decimal number, int digits)
    {
        if (digits > 28)
        {
            return true;
        }

        var bound = 1m;
        for (var i = 0; i < digits; i++)
        {
            bound *= 10;
        }

        return Math.Abs(decimal.Truncate(number)) < bound;
    }
}
