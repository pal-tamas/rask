using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Rask.Background;

/// <summary>
///     Reads lanes as <see cref="JobLane" />s: every lane asked for in one query, on the <c>(Lane, ProcessedAt)</c> index. A
///     claimed row runs — unless its work has already ended on this host and only its outcome is still being saved; the lane's
///     other rows that will still run wait, in the order the claim takes them (<c>RunAt</c>, then <c>Id</c>).
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext" /> that owns the jobs table.</typeparam>
internal sealed class JobLaneView<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider, JobsOptions options)
    where TContext : DbContext
{
    public async Task<IReadOnlyDictionary<string, JobLane>> Read(IEnumerable<string> lanes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        var names = lanes.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, JobLane>(StringComparer.Ordinal);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var max = options.MaxAttempts;
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);

        // A dead letter never runs again — but the attempt that made it one is still running while its row is claimed.
        var rows = await db.Set<Job>()
            .AsNoTracking()
            .Where(j => j.Lane != null
                && names.Contains(j.Lane)
                && j.ProcessedAt == null
                && (j.Attempts < max || j.ClaimedUntil > now))
            .OrderBy(j => j.RunAt)
            .ThenBy(j => j.Id)
            .Select(j => new Row(j.Id, j.Lane!, j.Type, j.Payload, j.RunAt, j.StartedAt, j.Attempts, j.ClaimedUntil, j.Stopped))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Fold(names, rows, now);
    }

    private Dictionary<string, JobLane> Fold(List<string> names, List<Row> rows, DateTime now)
    {
        var running = new Dictionary<string, LaneJob>(StringComparer.Ordinal);
        var waiting = names.ToDictionary(name => name, _ => new List<LaneJob>(), StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var here = options.Signals.Run(row.Id);
            if (here is { HasEnded: true })
            {
                continue; // its work is over here; only its outcome is still on the way to the row
            }

            var job = new LaneJob(
                row.Id, Read(row.Type, row.Payload), row.Type, row.RunAt, row.StartedAt, row.Attempts,
                Stopping: row.Stopped is not null || here?.Stopped is not null);
            if (row.ClaimedUntil > now && !running.ContainsKey(row.Lane))
            {
                running[row.Lane] = job;
            }
            else
            {
                waiting[row.Lane].Add(job);
            }
        }

        return names.ToDictionary(
            name => name,
            name => new JobLane(name, running.GetValueOrDefault(name), waiting[name]),
            StringComparer.Ordinal);
    }

    // A payload that no longer reads as a registered job is still listed, with no job: seen, rather than the view failing.
    private static IJob? Read(string type, string payload)
    {
        try
        {
            return JobSerializerRegistry.Deserialize(type, payload) as IJob;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record Row(
        long Id, string Lane, string Type, string Payload, DateTime RunAt, DateTime? StartedAt, int Attempts, DateTime? ClaimedUntil,
        JobStop? Stopped);
}
