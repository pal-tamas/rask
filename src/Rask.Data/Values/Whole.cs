using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The base of a value object that holds one whole number: <c>public sealed partial record Pieces() : Whole(Min: 0, Max: 1000);</c>
/// </summary>
/// <remarks>
///     The generated half is <see cref="Text" />'s, for a <see cref="long" />: <c>Pieces.Create(value)</c> checks the
///     rules. Two values are equal when they are of the same type and hold the same number.
/// </remarks>
public abstract record Whole
{
    private readonly long? _min;
    private readonly long? _max;

    /// <summary>Declares the rules: <c>Whole(Min: 0, Max: 1000)</c>.</summary>
    /// <param name="Min">The least it may be, or null.</param>
    /// <param name="Max">The greatest it may be, or null.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="Min" /> exceeds <paramref name="Max" />.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Whole(Min: 0, Max: 1000)`
    protected Whole(long? Min = null, long? Max = null)
#pragma warning restore IDE1006
    {
        if (Min > Max)
        {
            throw new ArgumentOutOfRangeException(nameof(Min), Min, "The least a number may be is above the greatest.");
        }

        _min = Min;
        _max = Max;
    }

    /// <summary>The number it holds.</summary>
    public long Value { get; protected init; }

    /// <summary>The least it may be, or null.</summary>
    public long? Min => _min;

    /// <summary>The greatest it may be, or null.</summary>
    public long? Max => _max;

    /// <summary>The same type, holding the same number.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>Whether the two are the same value.</returns>
    public virtual bool Equals(Whole? other) =>
        ReferenceEquals(this, other) || (other is not null && EqualityContract == other.EqualityContract && Value == other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Value);

    /// <summary>The number it holds, written in the current culture.</summary>
    /// <returns>The number as text.</returns>
    public sealed override string ToString() => Value.ToString(CultureInfo.CurrentCulture);

    /// <summary>What a given number is kept as: itself, unless a value object says otherwise.</summary>
    /// <param name="value">The number as given.</param>
    /// <returns>The number to check and keep.</returns>
    protected virtual long Normalize(long value) => value;

    /// <summary>The rules <paramref name="value" /> breaks, after <see cref="Normalize" />. Asked by generated code.</summary>
    /// <param name="value">The number as given, or null for none.</param>
    /// <returns>Each rule it breaks; none when it may be kept.</returns>
    protected IEnumerable<ValueProblem> Problems(long? value)
    {
        if (value is not { } given)
        {
            return [new ValueProblem(ValueProblemKind.Required)];
        }

        var number = Normalize(given);

        if (number < _min)
        {
            return [new ValueProblem(ValueProblemKind.BelowMin, _min)];
        }

        return number > _max ? [new ValueProblem(ValueProblemKind.AboveMax, _max)] : [];
    }
}
