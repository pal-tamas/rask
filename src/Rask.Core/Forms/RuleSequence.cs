namespace Rask.Core.Forms;

// Two rules as one: the first, then the second only if the first let the value through. What
// `.Validate(a).Validate(b)` leaves on a control — the edit context still registers one rule per field.
//
// Two synchronous rules stay a synchronous rule, so a field with no awaited check keeps the edit context's
// fast path; one asynchronous rule makes the pair asynchronous.
internal sealed class RuleSequence<T>
{
    private readonly Delegate _first;
    private readonly Delegate _second;

    private RuleSequence(Delegate first, Delegate second)
    {
        _first = first;
        _second = second;
    }

    public static Delegate Of(Delegate first, Delegate second)
    {
        var both = new RuleSequence<T>(first, second);

        return first is Validate<T> && second is Validate<T>
            ? new Validate<T>(both.Run)
            : new Func<T, ValueTask<IEnumerable<string>>>(both.RunAwaited);
    }

    private IEnumerable<string> Run(T value)
    {
        var rejected = Messages(((Validate<T>)_first)(value));

        return rejected.Count > 0 ? rejected : ((Validate<T>)_second)(value);
    }

    private async ValueTask<IEnumerable<string>> RunAwaited(T value)
    {
        var rejected = Messages(await Ask(_first, value).ConfigureAwait(false));

        // A later edit has superseded this check: the second rule — a lookup, usually — is not started for it.
        return rejected.Count > 0 || Ambient.CancellationToken.IsCancellationRequested
            ? rejected
            : await Ask(_second, value).ConfigureAwait(false);
    }

    private static ValueTask<IEnumerable<string>> Ask(Delegate rule, T value) => rule switch
    {
        Validate<T> now => new ValueTask<IEnumerable<string>>(now(value)),
        Func<T, ValueTask<IEnumerable<string>>> awaited => awaited(value),
        _ => throw Callback.Unexpected(rule),
    };

    // Read once: a rule may hand back an iterator, and asking it whether it has anything runs it.
    private static IReadOnlyCollection<string> Messages(IEnumerable<string>? messages) => messages switch
    {
        null => [],
        IReadOnlyCollection<string> counted => counted,
        _ => [.. messages],
    };
}
