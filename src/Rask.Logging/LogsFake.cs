using Rask.Batteries;

namespace Rask.Logging;

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
