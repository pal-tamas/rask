using System.Collections;

namespace Rask.Logging;

/// <summary>
///     The ambient stack of open <see cref="Microsoft.Extensions.Logging.ILogger.BeginScope{TState}" />
///     scopes, and the snapshot a log call takes of it.
/// </summary>
/// <remarks>
///     <para>
///         An <see cref="AsyncLocal{T}" /> linked list rather than a mutable collection: a scope opened on
///         one request must not be visible to another, and the list flows into awaited continuations the
///         way the surrounding request does. Disposing restores the previous head, so an out-of-order
///         dispose truncates rather than corrupting.
///     </para>
///     <para>
///         <b>What happens on the request thread, and what does not.</b> The snapshot flattens the stack
///         into a small array and calls <c>ToString()</c> on each value — that has to happen here, because
///         scope state is short-lived and may be mutated or pooled the moment the scope closes. Turning
///         that array into JSON does <em>not</em>; the writer does it on its own thread, off the request
///         path, in keeping with "a log call never waits on the disk".
///     </para>
/// </remarks>
internal static class LogScopes
{
    private static readonly AsyncLocal<Scope?> Current = new();

    /// <summary>Opens a scope. Disposing it restores the previous one.</summary>
    internal static IDisposable Push(object? state)
    {
        var scope = new Scope(state, Current.Value);
        Current.Value = scope;
        return scope;
    }

    /// <summary>
    ///     Flattens the open scopes, outermost first, or returns <c>null</c> when none are open — the
    ///     common case, which must cost nothing but one <see cref="AsyncLocal{T}" /> read.
    /// </summary>
    /// <param name="maxValues">
    ///     Upper bound on captured pairs. A scope stack is bounded in practice, but a log store must not
    ///     be a way for a runaway loop of nested scopes to consume memory per entry.
    /// </param>
    /// <param name="maxValueLength">Upper bound on each value, so one huge object can't dominate a row.</param>
    internal static IReadOnlyList<LogScopeValue>? Capture(int maxValues, int maxValueLength)
    {
        var head = Current.Value;
        if (head is null)
        {
            return null;
        }

        // Outermost first: the request id is more useful ahead of the innermost detail, and it reads the
        // way the code nests.
        var stack = new List<Scope>();
        for (var s = head; s is not null; s = s.Parent)
        {
            stack.Add(s);
        }

        stack.Reverse();

        var captured = new List<LogScopeValue>();
        foreach (var scope in stack)
        {
            if (captured.Count >= maxValues)
            {
                break;
            }

            Flatten(scope.State, captured, maxValues, maxValueLength);
        }

        return captured.Count == 0 ? null : captured;
    }

    private static void Flatten(object? state, List<LogScopeValue> into, int maxValues, int maxValueLength)
    {
        switch (state)
        {
            case null:
                return;

            // The shape BeginScope("{RequestId} {UserId}", a, b) and BeginScope(new Dictionary<,>) both
            // produce. Preferred over ToString() because it is the structured state the caller meant.
            case IReadOnlyList<KeyValuePair<string, object?>> pairs:
                foreach (var pair in pairs)
                {
                    if (into.Count >= maxValues)
                    {
                        return;
                    }

                    // The original format string is the template, not data — storing it would put
                    // "{RequestId} {UserId}" in every row alongside the values it formats.
                    if (pair.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    into.Add(new LogScopeValue(pair.Key, Truncate(pair.Value?.ToString(), maxValueLength)));
                }

                return;

            case IEnumerable<KeyValuePair<string, object?>> pairs:
                foreach (var pair in pairs)
                {
                    if (into.Count >= maxValues)
                    {
                        return;
                    }

                    if (pair.Key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    into.Add(new LogScopeValue(pair.Key, Truncate(pair.Value?.ToString(), maxValueLength)));
                }

                return;

            // A bare scope — BeginScope("checkout"). Still worth keeping: it is how most people reach for
            // this before they discover structured state.
            default:
                into.Add(new LogScopeValue(
                    LogScopeValue.MessageKey,
                    Truncate(state.ToString(), maxValueLength)));
                return;
        }
    }

    private static string Truncate(string? value, int max) =>
        value is null ? string.Empty
        : value.Length <= max ? value
        : value[..max];

    private sealed class Scope(object? state, Scope? parent) : IDisposable
    {
        private bool _disposed;

        internal object? State { get; } = state;
        internal Scope? Parent { get; } = parent;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = Parent;
        }
    }
}
