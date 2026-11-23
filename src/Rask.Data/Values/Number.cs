using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one decimal number: <c>public sealed partial record Litres() : Number(Min: 1, Scale: 2);</c>
/// </summary>
/// <remarks>
///     The generated half is <see cref="Text" />'s, for a <see cref="decimal" />: <c>Litres.Create(value)</c> checks
///     the rules, and the column is given <see cref="Precision" /> and <see cref="Scale" /> when either is declared.
///     Two values are equal when they are of the same type and hold the same number.
/// </remarks>
public abstract record Number
{
    private readonly decimal? _min;
    private readonly decimal? _max;
    private readonly int? _scale;
    private readonly int? _precision;

    /// <summary>Declares the rules: <c>Number(Min: 1, Scale: 2)</c>.</summary>
    /// <param name="Min">The least it may be, or null.</param>
    /// <param name="Max">The greatest it may be, or null.</param>
    /// <param name="Scale">The most decimal places it may have, and the column's scale; or null.</param>
    /// <param name="Precision">The most digits it may have in all, and the column's precision; or null.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="Min" /> exceeds <paramref name="Max" />, or the precision and scale cannot describe a number.
    /// </exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Number(Min: 1, Scale: 2)`
    protected Number(decimal? Min = null, decimal? Max = null, int? Scale = null, int? Precision = null)
#pragma warning restore IDE1006
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

        _min = Min;
        _max = Max;
        _scale = Scale;
        _precision = Precision;
    }

    /// <summary>The number it holds.</summary>
    public decimal Value { get; protected init; }

    /// <summary>The least it may be, or null.</summary>
    public decimal? Min => _min;

    /// <summary>The greatest it may be, or null.</summary>
    public decimal? Max => _max;

    /// <summary>The most decimal places it may have, and its column's scale; or null.</summary>
    public int? Scale => _scale;

    /// <summary>The most digits it may have in all, and its column's precision; or null.</summary>
    public int? Precision => _precision;

    /// <summary>The same type, holding the same number.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>Whether the two are the same value.</returns>
    public virtual bool Equals(Number? other) =>
        ReferenceEquals(this, other) || (other is not null && EqualityContract == other.EqualityContract && Value == other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Value);

    /// <summary>The number it holds, written in the current culture.</summary>
    /// <returns>The number as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.CurrentCulture);

    /// <summary>What a given number is kept as: itself, unless a value object says otherwise.</summary>
    /// <param name="value">The number as given.</param>
    /// <returns>The number to check and keep.</returns>
    protected virtual decimal Normalize(decimal value) => value;

    /// <summary>The rules <paramref name="value" /> breaks, after <see cref="Normalize" />. Asked by generated code.</summary>
    /// <param name="value">The number as given, or null for none.</param>
    /// <returns>Each rule it breaks; none when it may be kept.</returns>
    protected IEnumerable<ValueProblem> Problems(decimal? value)
    {
        if (value is not { } given)
        {
            return [new ValueProblem(ValueProblemKind.Required)];
        }

        var number = Normalize(given);
        List<ValueProblem>? problems = null;

        if (number < _min)
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.BelowMin, _min));
        }

        if (number > _max)
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.AboveMax, _max));
        }

        if (_scale is { } scale && decimal.Round(number, scale) != number)
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.TooManyDecimals, scale));
        }

        if (_precision is { } precision && !FitsDigits(number, precision - (_scale ?? 0)))
        {
            (problems ??= []).Add(new ValueProblem(ValueProblemKind.TooManyDigits));
        }

        return problems is null ? [] : problems;
    }

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
