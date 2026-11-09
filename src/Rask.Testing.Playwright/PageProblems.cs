using System.Collections;
using System.Collections.Concurrent;

namespace Rask.Testing;

/// <summary>
///     What went wrong on a page while a test drove it, one sentence each: <c>var problems = site.Watch(page);</c> …
///     <c>Assert.Empty(problems);</c>
/// </summary>
/// <remarks>
///     A console error, an error the page threw, and a request to the app that failed or answered 400 or worse. Requests
///     to other origins are the page's business with them, not the app's, and are not counted.
/// </remarks>
public sealed class PageProblems : IEnumerable<string>
{
    private readonly ConcurrentQueue<string> _seen = new();
    private volatile bool _closed;

    internal PageProblems()
    {
    }

    /// <summary>How many problems there have been.</summary>
    public int Count => _seen.Count;

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => _seen.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Every problem, one per line.</summary>
    public override string ToString() => string.Join(Environment.NewLine, _seen);

    internal void Add(string problem)
    {
        if (!_closed)
        {
            _seen.Enqueue(problem);
        }
    }

    // Closing the page aborts its requests and its connection; none of that is the page's doing.
    internal void Close() => _closed = true;
}
