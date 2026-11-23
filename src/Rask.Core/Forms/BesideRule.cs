using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Rask.Core.Forms;

// A type-carried rule that reads the fields beside its own — a range's end, asked against its start: registered as a
// Func<object, T, IEnumerable<string>> that is handed the object holding the field first. A bound field asks for its
// rule on every render, so the rule over one holder is made once and kept for as long as the holder is.
internal static class BesideRule
{
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<Delegate, Delegate>> Kept = [];

    // Whether the rule reads the fields beside its own: over any field type, so a mismatch is told apart from no rule.
    public static bool Reads(Delegate rule) =>
        rule.GetType() is { IsGenericType: true } shape
        && shape.GetGenericTypeDefinition() == typeof(Func<,,>)
        && shape.GetGenericArguments() is [var holder, _, var told]
        && holder == typeof(object)
        && told == typeof(IEnumerable<string>);

    // The rule as a field over T asks it, the holder handed in; null when it is over another type than T.
    public static Delegate? Over<T>(Delegate rule, object holder) =>
        rule is Func<object, T, IEnumerable<string>>
            ? Kept.GetValue(holder, static _ => new ConcurrentDictionary<Delegate, Delegate>())
                .GetOrAdd(rule, static (rule, holder) => Bound<T>((Func<object, T, IEnumerable<string>>)rule, holder), holder)
            : null;

    private static Validate<T> Bound<T>(Func<object, T, IEnumerable<string>> rule, object holder) => value => rule(holder, value);
}
