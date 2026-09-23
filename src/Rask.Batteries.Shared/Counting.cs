using System.Diagnostics.CodeAnalysis;

namespace Rask.Batteries;

/// <summary>
///     What a fake battery recorded, narrowed by the steps taken so far and waiting for the count that
///     asserts: <c>mail.Sent().To("ann@x.io").Once()</c>.
/// </summary>
/// <typeparam name="T">What was recorded — an email, a job.</typeparam>
/// <remarks>
///     Each narrowing step hands back a new <see cref="Counting{T}" />; the counting steps
///     (<see cref="Once" />, <see cref="Twice" />, <see cref="None" />, <see cref="Exactly" />) are what
///     actually check, and they throw naming everything that <em>was</em> recorded — because the useful
///     half of a failing expectation is what happened instead.
/// </remarks>
public readonly struct Counting<T>
{
    private readonly IReadOnlyList<T> _all;
    private readonly IReadOnlyList<T> _matching;
    private readonly string _noun;
    private readonly string _verb;
    private readonly string _narrowing;
    private readonly string? _whenNoneAtAll;
    private readonly Func<T, string> _describe;

    /// <summary>Starts a sentence over everything <paramref name="all" /> recorded.</summary>
    /// <param name="all">Everything the fake recorded of this kind, in order.</param>
    /// <param name="noun">What one recording is called: "email", "SendWelcome".</param>
    /// <param name="verb">What happened to it: "sent", "enqueued".</param>
    /// <param name="describe">How one recording reads in a failure message.</param>
    /// <param name="whenNoneAtAll">
    ///     What to say when <paramref name="all" /> is empty — the place to name what happened
    ///     <em>instead</em>, when the fake filtered by kind and holds other kinds it cannot type here.
    /// </param>
    public Counting(
        IReadOnlyList<T> all, string noun, string verb, Func<T, string> describe, string? whenNoneAtAll = null)
        : this(all, all, noun, verb, "", whenNoneAtAll, describe)
    {
    }

    private Counting(
        IReadOnlyList<T> all,
        IReadOnlyList<T> matching,
        string noun,
        string verb,
        string narrowing,
        string? whenNoneAtAll,
        Func<T, string> describe)
    {
        _all = all;
        _matching = matching;
        _noun = noun;
        _verb = verb;
        _narrowing = narrowing;
        _whenNoneAtAll = whenNoneAtAll;
        _describe = describe;
    }

    /// <summary>How many were recorded that match every step taken so far.</summary>
    public int Count => _matching.Count;

    /// <summary>Narrows to what <paramref name="predicate" /> accepts, saying so in <paramref name="said" />.</summary>
    /// <param name="predicate">What to keep.</param>
    /// <param name="said">How the step reads in a failure message, e.g. <c>to "ann@x.io"</c>.</param>
    public Counting<T> Where(Func<T, bool> predicate, string said)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new Counting<T>(
            _all,
            [.. _matching.Where(predicate)],
            _noun,
            _verb,
            _narrowing.Length == 0 ? said : $"{_narrowing} {said}",
            _whenNoneAtAll,
            _describe);
    }

    /// <summary>Asserts exactly one matched.</summary>
    public void Once() => Exactly(1);

    /// <summary>Asserts exactly two matched.</summary>
    public void Twice() => Exactly(2);

    /// <summary>Asserts none matched.</summary>
    public void None() => Exactly(0);

    /// <summary>Asserts exactly <paramref name="times" /> matched.</summary>
    public void Exactly(int times)
    {
        if (_matching.Count != times)
        {
            throw new CountingException(Explain(times));
        }
    }

    /// <summary>
    ///     The one that matched, to assert on what the steps do not cover — the body's text, a job's
    ///     property. Throws unless exactly one matched.
    /// </summary>
    public T Single()
    {
        Once();
        return _matching[0];
    }

    private string Explain(int expected)
    {
        var what = _narrowing.Length == 0 ? _noun : $"{_noun} {_narrowing}";
        var head = expected == 1
            ? $"Expected one {what}; {Count} was {_verb}."
            : $"Expected {expected} × {what}; {Count} was {_verb}.";

        if (Count is not 1)
        {
            head = head.Replace($"{Count} was", $"{Count} were", StringComparison.Ordinal);
        }

        return _all.Count == 0
            ? $"{head} {_whenNoneAtAll ?? $"Nothing was {_verb} at all."}"
            : $"{head} All {_all.Count} {_verb}: {string.Join("; ", _all.Select(_describe))}.";
    }
}

/// <summary>A fake battery's expectation that did not hold.</summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Only a Counting step throws this, always with the message it built.")]
public sealed class CountingException(string message) : Exception(message);
