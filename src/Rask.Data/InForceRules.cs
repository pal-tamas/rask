using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     The condition each <see cref="OneInForce{TChild}" /> child declared, compiled once, for the aggregate to ask of
///     the children in hand.
/// </summary>
/// <remarks>Remembered as the model is built: the declaration lives in the child's <c>Configure</c>, and nowhere else.</remarks>
internal static class InForceRules
{
    private static readonly ConcurrentDictionary<Type, Delegate> Declared = new();

    /// <summary>Records the condition <typeparamref name="TChild" /> is in force under.</summary>
    internal static void Remember<TChild>(Expression<Func<TChild, bool>> inForce) =>
        Declared[typeof(TChild)] = inForce.Compile(preferInterpretation: true);

    /// <summary>The condition <typeparamref name="TChild" /> is in force under.</summary>
    /// <exception cref="InvalidOperationException">It declared none, or no model with it has been built yet.</exception>
    internal static Func<TChild, bool> For<TChild>() =>
        Declared.TryGetValue(typeof(TChild), out var rule)
            ? (Func<TChild, bool>)rule
            : throw new InvalidOperationException(
                $"'{typeof(TChild).Name}' says nothing about when it is in force. Declare it in its Configure — " +
                $"b.InForceWhile(x => x.EndedAt == null) — and build the model (open the database) before asking.");
}
