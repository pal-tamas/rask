using System.ComponentModel;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Values;

/// <summary>
///     A value object being made by the generated <c>TryCreate</c>: while it is open, a refused value records its
///     problems here instead of throwing, so asking whether a value may be kept costs no exception. Generated code
///     opens one through <see cref="GeneratedValueRules.Attempt" />; not an API to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly ref struct ValueAttempt
{
    [ThreadStatic]
    private static Collector? t_current;

    private readonly Collector? _collector;

    private ValueAttempt(Collector collector) => _collector = collector;

    /// <summary>What the value objects made since the attempt opened were refused for; empty when none were.</summary>
    public IReadOnlyList<Problem> Problems => _collector?.Problems is { } problems ? problems : [];

    /// <summary>Closes the attempt: a value refused after this throws again.</summary>
    public void Dispose() => End(_collector);

    private static void End(Collector? collector)
    {
        if (collector is not null)
        {
            t_current = collector.Previous;
        }
    }

    internal static ValueAttempt Begin()
    {
        var collector = new Collector(t_current);
        t_current = collector;
        return new ValueAttempt(collector);
    }

    // Recorded by the attempt in flight on this thread, or thrown: a constructor has nowhere else to say it.
    internal static void Refuse(List<Problem> problems)
    {
        if (t_current is { } collector)
        {
            (collector.Problems ??= []).AddRange(problems);
            return;
        }

        throw new RaskValidationException([.. problems.Select(static p => new FieldFailure(p.Message, p.Field is { } field ? [field] : []))]);
    }

    private sealed class Collector(Collector? previous)
    {
        public Collector? Previous { get; } = previous;

        public List<Problem>? Problems { get; set; }
    }
}
