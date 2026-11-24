using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one decimal number:
///     <c>public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);</c>
/// </summary>
/// <remarks>
///     The constructor checks the bounds and the digits and throws a <see cref="Rask.Cqrs.RaskValidationException" />
///     when one refuses the number — or, declared <c>Round: true</c>, first rounds it to its scale, so
///     <c>Number(Value, Scale: 2, Round: true)</c> keeps <c>1.005m</c> as <c>1.01m</c>; the generated half is <see cref="Text" />'s, for a <see cref="decimal" />. The
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
    /// <param name="Round">
    ///     Whether a number with more decimal places than <paramref name="Scale" /> is rounded to it — half away from zero,
    ///     as SQL Server rounds what it stores — before it is checked and kept, instead of refused: what a computed
    ///     number (a unit price, a share of a total) wants, where nobody typed the digits. Needs a scale.
    /// </param>
    /// <param name="Message">
    ///     What a number that breaks a rule declared here is told, said once however many it breaks; or null for the
    ///     framework's words for each.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="Min" /> exceeds <paramref name="Max" />, the precision and scale cannot describe a number, or
    ///     <paramref name="Round" /> is asked with no <paramref name="Scale" /> to round to.
    /// </exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the number.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Number(Value, Min: 1, Scale: 2)`
    protected Number(decimal Value, decimal? Min = null, decimal? Max = null, int? Scale = null, int? Precision = null, bool Round = false, string? Message = null)
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

        if (Round && Scale is null)
        {
            throw new ArgumentOutOfRangeException(nameof(Round), Round, "A number is rounded to its scale: declare a Scale beside Round: true.");
        }

        if (DeclaredRules.IsReading)
        {
            DeclaredRules.Note(new NumberRules(Min, Max, Scale, Precision));
        }

        if (Loading.IsOn)
        {
            this.Value = Value;
            return;
        }

        this.Value = Round && Scale is { } places ? decimal.Round(Value, places, MidpointRounding.AwayFromZero) : Value;
        Check(ValueWords.Once(Problems(this.Value, Min, Max, Scale, Precision, Message), Message));
    }

    /// <summary>The number it holds.</summary>
    public decimal Value { get; }

    /// <summary>The number it holds, written in the current culture.</summary>
    /// <returns>The number as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.CurrentCulture);

    private static List<Problem>? Problems(decimal number, decimal? min, decimal? max, int? scale, int? precision, string? message)
    {
        List<Problem>? problems = null;

        if (number < min)
        {
            (problems ??= []).Add(Say(ValueProblemKind.BelowMin, message, min));
        }

        if (number > max)
        {
            (problems ??= []).Add(Say(ValueProblemKind.AboveMax, message, max));
        }

        if (scale is { } places && decimal.Round(number, places) != number)
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooManyDecimals, message, places));
        }

        if (precision is { } digits && !FitsDigits(number, digits - (scale ?? 0)))
        {
            (problems ??= []).Add(Say(ValueProblemKind.TooManyDigits, message));
        }

        return problems;
    }

    private static Problem Say(ValueProblemKind kind, string? message, object? limit = null) =>
        Problem.Whole(ValueWords.Say(new ValueProblem(kind, limit, message)));

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
