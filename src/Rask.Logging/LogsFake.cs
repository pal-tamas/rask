using Microsoft.Extensions.Logging;
using Rask.Batteries;

namespace Rask.Logging;

/// <summary>A test's stand-in for the durable log: <c>using var logs = Logs.Fake();</c>.</summary>
public static class LogsFakes
{
    extension(Logs)
    {
        /// <summary>
        ///     Takes the place of the log store for this test — an in-memory one that really stores, so a
        ///     search reads back what was written — until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var logs = Logs.Fake();
        ///
        ///     await page.Click("Save");
        ///
        ///     logs.Stored().AtLeast(LogLevel.Error).Saying("refused").Once();
        ///     </code>
        ///     <para>
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's
        ///         entries. It stands in front of <c>Logs.Search</c>; a class that takes <see cref="ILogs" />
        ///         in its constructor is handed whatever the container holds, so register the fake there too
        ///         — <c>services.AddSingleton&lt;ILogs&gt;(logs)</c> — when the code under test injects it.
        ///     </para>
        ///     <para>
        ///         It records what reached the STORE, not what was logged: the real pillar drains an
        ///         <c>ILogger</c> through a channel and a background writer, so a test that wants to prove a
        ///         line was logged should append to this directly rather than expect a logger call to arrive.
        ///     </para>
        /// </remarks>
        public static LogsFake Fake() => new();
    }
}

/// <summary>An in-memory log store that remembers what a test wrote to it.</summary>
public sealed class LogsFake : ILogs, IDisposable
{
    private readonly List<LogRecord> _stored = [];
    private readonly ILogs? _previous;
    private readonly Lock _gate = new();

    internal LogsFake()
    {
        _previous = Logs.Faked.Value;
        Logs.Faked.Value = this;
    }

    /// <summary>
    ///     Asks about what was stored: <c>logs.Stored().AtLeast(LogLevel.Error).Once()</c>,
    ///     <c>logs.Stored().Saying("refused").Once()</c>, <c>logs.Stored().None()</c>.
    /// </summary>
    public Counting<LogRecord> Stored()
    {
        lock (_gate)
        {
            return new Counting<LogRecord>(
                [.. _stored], "entry", "stored", static e => $"{e.Level} {e.Category}: \"{e.Message}\"");
        }
    }

    /// <summary>Empties it, without putting the real store back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _stored.Clear();
        }
    }

    /// <summary>Puts the real log store back.</summary>
    public void Dispose() => Logs.Faked.Value = _previous;

    /// <inheritdoc />
    public Task Append(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            _stored.AddRange(records);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<LogPage> Search(LogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            var matching = _stored.Where(e => Matches(e, query)).Reverse().ToArray();
            var page = Math.Max(1, query.Page);
            var size = Math.Max(1, query.PageSize);
            return Task.FromResult(new LogPage(
                [.. matching.Skip((page - 1) * size).Take(size)], matching.Length, page, size));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> Categories(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                [.. _stored.Select(e => e.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task<long> Count(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult((long)_stored.Count);
        }
    }

    /// <inheritdoc />
    public Task<int> Trim(TimeSpan? olderThan, int? keepNewest, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var before = _stored.Count;

            if (olderThan is { } age)
            {
                var cutoff = Clock.Now - age;
                _stored.RemoveAll(e => e.Timestamp < cutoff);
            }

            if (keepNewest is { } rows && _stored.Count > rows)
            {
                _stored.RemoveRange(0, _stored.Count - rows);
            }

            return Task.FromResult(before - _stored.Count);
        }
    }

    /// <inheritdoc />
    public Task Clear(CancellationToken cancellationToken = default)
    {
        Clear();
        return Task.CompletedTask;
    }

    private static bool Matches(LogRecord entry, LogQuery query) =>
        (query.MinimumLevel is not { } level || entry.Level >= level)
        && (query.Category is not { } category
            || entry.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
        && (query.Search is not { } text
            || entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (entry.Exception?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
        && (query.From is not { } from || entry.Timestamp >= from)
        && (query.To is not { } to || entry.Timestamp <= to);
}

/// <summary>The steps that narrow what a test asks about its log.</summary>
public static class StoredLogCounting
{
    extension(Counting<LogRecord> stored)
    {
        /// <summary>Only entries at or above <paramref name="level" />.</summary>
        public Counting<LogRecord> AtLeast(LogLevel level) =>
            stored.Where(e => e.Level >= level, $"at {level} or above");

        /// <summary>Only entries whose message or exception contains <paramref name="text" />.</summary>
        public Counting<LogRecord> Saying(string text) =>
            stored.Where(
                e => e.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (e.Exception?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false),
                $"saying \"{text}\"");

        /// <summary>Only entries logged under <paramref name="category" />.</summary>
        public Counting<LogRecord> From(string category) =>
            stored.Where(e => e.Category.Contains(category, StringComparison.OrdinalIgnoreCase), $"from {category}");
    }
}
