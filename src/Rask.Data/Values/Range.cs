using System.Globalization;
using System.Runtime.CompilerServices;

namespace Rask.Data;

/// <summary>
///     The base of a value object that is a start and an end:
///     <c>public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);</c>
/// </summary>
/// <remarks>
///     <para>
///         The derived record keeps its own typed members — <c>Start</c> and <c>End</c>, <c>From</c> and <c>To</c> —
///         and the constructor checks them: the end not before the start (after it, when <c>Strict</c>), and both
///         within <c>Min</c> and <c>Max</c>. A null end is an open range, and is not compared. A refusal is shown
///         under the member it is about, named as the record hands it over: <c>"End"</c>, and through
///         <c>KmRange.Create(10, 5, "Km")</c>, <c>"Km.End"</c>.
///     </para>
///     <para>
///         Stored as the record's own columns, like any value object of several members; loaded as it was stored.
///     </para>
/// </remarks>
public abstract record Range : Value
{
    /// <summary>Checks the range the derived record holds.</summary>
    /// <param name="Start">The start; the derived record's own positional member.</param>
    /// <param name="End">The end, or null for a range open at its end.</param>
    /// <param name="Strict">Whether the end must be after the start, rather than at least the start.</param>
    /// <param name="Min">The least either end may be, of the ends' type (a number is converted to it); or null.</param>
    /// <param name="Max">The greatest either end may be, of the ends' type (a number is converted to it); or null.</param>
    /// <param name="startMember">Filled in by the compiler: the member <paramref name="Start" /> is read from.</param>
    /// <param name="endMember">Filled in by the compiler: the member <paramref name="End" /> is read from.</param>
    /// <exception cref="ArgumentException">The ends, or a bound and the ends, are of types that cannot be compared.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">The range breaks a rule.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Range(Start, End, Strict: true)`
    protected Range(
        IComparable Start,
        IComparable? End,
        bool Strict = false,
        IComparable? Min = null,
        IComparable? Max = null,
        [CallerArgumentExpression(nameof(Start))] string startMember = "",
        [CallerArgumentExpression(nameof(End))] string endMember = "")
#pragma warning restore IDE1006
        : base(checkNow: false)
    {
        var startName = Problem.MemberName(startMember);
        if (Start is null)
        {
            Check(Loading.IsOn ? null : [new Problem(startName, ValueWords.Say(new ValueProblem(ValueProblemKind.Required)))]);
            return;
        }

        var type = Start.GetType();
        if (End is not null && End.GetType() != type)
        {
            throw new ArgumentException(
                $"A range's ends are of one type: the start is a {type.Name}, the end a {End.GetType().Name}.", nameof(End));
        }

        var least = Aligned(Min, type, nameof(Min));
        var most = Aligned(Max, type, nameof(Max));
        if (Loading.IsOn)
        {
            return;
        }

        List<Problem>? problems = null;
        Bounded(ref problems, Start, startName, least, most);
        if (End is not null)
        {
            var endName = Problem.MemberName(endMember);
            var order = End.CompareTo(Start);
            if (Strict ? order <= 0 : order < 0)
            {
                (problems ??= []).Add(Say(endName, Strict ? ValueProblemKind.EndNotAfterStart : ValueProblemKind.EndBeforeStart, Start));
            }

            Bounded(ref problems, End, endName, least, most);
        }

        Check(problems);
    }

    private static void Bounded(ref List<Problem>? problems, IComparable end, string? name, IComparable? least, IComparable? most)
    {
        if (least is not null && end.CompareTo(least) < 0)
        {
            (problems ??= []).Add(Say(name, ValueProblemKind.BelowMin, least));
        }

        if (most is not null && end.CompareTo(most) > 0)
        {
            (problems ??= []).Add(Say(name, ValueProblemKind.AboveMax, most));
        }
    }

    // `Min: 1` on a range of decimals: a number is converted to the ends' type; anything else must already be it.
    private static IComparable? Aligned(IComparable? bound, Type type, string name)
    {
        if (bound is null || bound.GetType() == type)
        {
            return bound;
        }

        if (bound is IConvertible && IsNumber(type) && IsNumber(bound.GetType()))
        {
            return (IComparable)Convert.ChangeType(bound, type, CultureInfo.InvariantCulture);
        }

        throw new ArgumentException($"A range's bound is of its ends' type, a {type.Name}; this one is a {bound.GetType().Name}.", name);
    }

    private static bool IsNumber(Type type) =>
        Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal;

    private static Problem Say(string? name, ValueProblemKind kind, object limit) =>
        new(name, ValueWords.Say(new ValueProblem(kind, limit)));
}
