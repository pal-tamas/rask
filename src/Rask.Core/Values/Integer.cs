using System.Globalization;

namespace Rask.Values;

/// <summary>
///     The base of a value object that holds one whole number, an <see cref="int" /> or a <see cref="long" />:
///     <c>public sealed partial record Pieces(long Value) : Integer(Value, Min: 0, Max: 1000);</c>,
///     <c>public sealed partial record Rank(int Value) : Integer(Value, Min: 1);</c>
/// </summary>
/// <remarks>
///     The derived record keeps its own typed <c>Value</c>, the <see cref="int" /> or <see cref="long" /> it declares;
///     the constructor checks the bounds and throws a <see cref="Rask.Cqrs.RaskValidationException" /> when one refuses
///     the number. The generated half is <see cref="Text" />'s, for the number's type. Two values are equal when they
///     are of the same type and hold the same number.
/// </remarks>
#pragma warning disable CA1720 // the owner's name for the base, beside Text and Number: what a value object declared from it holds
public abstract record Integer : Value
#pragma warning restore CA1720
{
    private readonly long _number;

    /// <summary>Keeps <paramref name="Value" /> once the bounds the other arguments declare accept it.</summary>
    /// <param name="Value">The number as given; the derived record's own positional parameter.</param>
    /// <param name="Min">The least it may be, or null.</param>
    /// <param name="Max">The greatest it may be, or null.</param>
    /// <param name="Message">
    ///     What a number that breaks a rule declared here is told, said once however many it breaks; or null for the
    ///     framework's words for each.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="Min" /> exceeds <paramref name="Max" />.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A bound refuses the number.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Integer(Value, Min: 0, Max: 1000)`
    protected Integer(long Value, long? Min = null, long? Max = null, string? Message = null)
#pragma warning restore IDE1006
        : base(checkNow: false)
    {
        if (Min > Max)
        {
            throw new ArgumentOutOfRangeException(nameof(Min), Min, "The least a number may be is above the greatest.");
        }

        if (DeclaredRules.IsReading)
        {
            DeclaredRules.Note(new IntegerRules(Min, Max));
        }

        _number = Value;
        if (RowLoading.IsOn)
        {
            return;
        }

        if (Value < Min)
        {
            Check([Problem.Whole(ValueWords.Say(new ValueProblem(ValueProblemKind.BelowMin, Min, Message)))]);
        }
        else if (Value > Max)
        {
            Check([Problem.Whole(ValueWords.Say(new ValueProblem(ValueProblemKind.AboveMax, Max, Message)))]);
        }
        else
        {
            Check(null);
        }
    }

    /// <summary>The number it holds, written culture-invariant: a value's text, not its display (that is the UI's).</summary>
    /// <returns>The number as text.</returns>
    public sealed override string ToString() => _number.ToString(CultureInfo.InvariantCulture);
}
