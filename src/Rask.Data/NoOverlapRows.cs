using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     The C# a rule's <c>Where(…)</c> was declared in, by entity and rule name, for the check a save asks before the
///     database does: the model keeps only its SQL.
/// </summary>
internal static class NoOverlapRows
{
    private static readonly ConcurrentDictionary<(Type Entity, string Rule), LambdaExpression> Declared = new();

    internal static void Remember(Type entity, string rule, LambdaExpression rows) => Declared[(entity, rule)] = rows;

    /// <summary>The rows <paramref name="rule" /> holds, or null for every row.</summary>
    internal static LambdaExpression? Of(Type entity, string rule) => Declared.GetValueOrDefault((entity, rule));
}
