using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

// ── The jobs the stop, limit and lane-view tests drive. Each parks until the test lets it go, and says how it ended in the
//    Recorder: "{tag}:done", "{tag}:stopped:{why}" (Jobs.Stopped as the handler read it) or "{tag}:cancelled:none". ─────────

/// <summary>Parks until released, passing <c>Current.Cancellation</c> on — the well-behaved job.</summary>
public sealed record ParkJob(string Tag) : IJob;

public sealed class ParkJobHandler(LaneGate lanes, Recorder recorder) : ICommandHandler<ParkJob>
{
    public async Task Handle(ParkJob command)
    {
        lanes.Enter(command.Tag);
        try
        {
            await lanes.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
            recorder.Add($"{command.Tag}:done");
        }
        catch (OperationCanceledException)
        {
            recorder.Add(StopWords.Of(command.Tag));
            throw;
        }
    }
}

/// <summary>Parks until released, its time limit read from the job itself: <c>.After(job =&gt; job.Minutes)</c>.</summary>
public sealed record CappedJob(string Tag, int Minutes) : IJob;

public sealed class CappedJobHandler(LaneGate lanes, Recorder recorder) : ICommandHandler<CappedJob>
{
    public async Task Handle(CappedJob command)
    {
        lanes.Enter(command.Tag);
        try
        {
            await lanes.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
            recorder.Add($"{command.Tag}:done");
        }
        catch (OperationCanceledException)
        {
            recorder.Add(StopWords.Of(command.Tag));
            throw;
        }
    }
}

/// <summary>Ignores its token: it returns only when released, whatever stopped it — and holds its lane until then.</summary>
public sealed record StubbornJob(string Tag) : IJob;

public sealed class StubbornJobHandler(LaneGate lanes, Recorder recorder) : ICommandHandler<StubbornJob>
{
    public async Task Handle(StubbornJob command)
    {
        lanes.Enter(command.Tag);
        await lanes.Released(command.Tag).ConfigureAwait(false);
        recorder.Add($"{command.Tag}:returned:{Jobs.Stopped?.ToString() ?? "none"}");
    }
}

/// <summary>Swallows its cancellation and returns as if it had finished.</summary>
public sealed record ShrugJob(string Tag) : IJob<string>;

public sealed class ShrugJobHandler(LaneGate lanes, Recorder recorder) : IJobHandler<ShrugJob, string>
{
    public async Task<string> Handle(ShrugJob job)
    {
        lanes.Enter(job.Tag);
        try
        {
            await lanes.Released(job.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            recorder.Add($"{job.Tag}:shrugged");
        }

        return "finished anyway";
    }
}

/// <summary>Throws something other than a cancellation once cancelled — a database call that gave up on a cancelled wait.</summary>
public sealed record GrumbleJob(string Tag) : IJob;

public sealed class GrumbleJobHandler(LaneGate lanes) : ICommandHandler<GrumbleJob>
{
    public async Task Handle(GrumbleJob command)
    {
        lanes.Enter(command.Tag);
        try
        {
            await lanes.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("The database closed the connection under a cancelled command.");
        }
    }
}

/// <summary>Writes a job row of its own, then parks: what it saved before a stop must stay saved.</summary>
public sealed record SavingJob(string Tag) : IJob;

public sealed class SavingJobHandler(LaneGate lanes, Recorder recorder) : ICommandHandler<SavingJob>
{
    public async Task Handle(SavingJob command)
    {
        await Jobs.Enqueue(new RecordJob($"{command.Tag}:saved")).ConfigureAwait(false);
        lanes.Enter(command.Tag);
        try
        {
            await lanes.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            recorder.Add(StopWords.Of(command.Tag));
            throw;
        }
    }
}

/// <summary>Reports progress each time the test asks it to (<see cref="Pulses" />), until it is stopped.</summary>
public sealed record ChattyJob(string Tag) : IJob;

public sealed class ChattyJobHandler(LaneGate lanes, Pulses pulses, Recorder recorder) : ICommandHandler<ChattyJob>
{
    public async Task Handle(ChattyJob command)
    {
        lanes.Enter(command.Tag);
        try
        {
            while (true)
            {
                var asked = await pulses.Next(command.Tag, Current.Cancellation).ConfigureAwait(false);
                Jobs.Report($"{command.Tag} got on");
                asked.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            recorder.Add(StopWords.Of(command.Tag));
            throw;
        }
    }
}

/// <summary>Per tag: the test asks a <see cref="ChattyJob" /> to report, and waits until it has.</summary>
public sealed class Pulses
{
    private readonly ConcurrentDictionary<string, Channel<TaskCompletionSource>> _asks = new(StringComparer.Ordinal);

    /// <summary>Makes the job of <paramref name="tag" /> report once, and waits until it has.</summary>
    public async Task Report(string tag)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Asks(tag).Writer.WriteAsync(done).ConfigureAwait(false);
        await done.Task.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling).ConfigureAwait(false);
    }

    public ValueTask<TaskCompletionSource> Next(string tag, CancellationToken cancellationToken) =>
        Asks(tag).Reader.ReadAsync(cancellationToken);

    private Channel<TaskCompletionSource> Asks(string tag) =>
        _asks.GetOrAdd(tag, _ => Channel.CreateUnbounded<TaskCompletionSource>());
}

/// <summary>How a parked job says it ended when its token was cancelled.</summary>
internal static class StopWords
{
    public static string Of(string tag) =>
        Jobs.Stopped is { } why ? $"{tag}:stopped:{why}" : $"{tag}:cancelled:none";
}

/// <summary>Reading and writing job rows the way an outsider — a script, the dashboard — would.</summary>
internal static class Rows
{
    /// <summary>The id of the one job whose payload names <paramref name="tag" />.</summary>
    public static async Task<long> IdOf(this JobsHarness h, string tag)
    {
        await using var db = h.NewContext();
        var rows = await db.Set<Job>().AsNoTracking().ToListAsync();
        return rows.Single(j => j.Payload.Contains($"\"{tag}\"", StringComparison.Ordinal)).Id;
    }

    public static async Task<Job?> Row(this JobsHarness h, long id)
    {
        await using var db = h.NewContext();
        return await db.Set<Job>().AsNoTracking().SingleOrDefaultAsync(j => j.Id == id);
    }

    /// <summary>Waits until <paramref name="value" /> is among what the handlers recorded.</summary>
    public static Task Recorded(this JobsHarness h, string value) =>
        h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Values.Contains(value)), expression: $"recorded '{value}'");

    /// <summary>Waits until job <paramref name="id" />'s row is processed — and gives it back.</summary>
    public static async Task<Job> Processed(this JobsHarness h, long id)
    {
        await h.WaitUntilAsync(async () => (await h.Row(id))?.ProcessedAt is not null, expression: $"job {id} processed");
        return (await h.Row(id))!;
    }

    /// <summary>Waits until a log line says <paramref name="words" />.</summary>
    public static Task Logged(this JobsHarness h, string words) =>
        h.WaitUntilAsync(() => Task.FromResult(h.Logs.Any(l => l.Contains(words, StringComparison.Ordinal))), expression: $"logged '{words}'");

    public static int LogCount(this JobsHarness h, string words) =>
        h.Logs.Count(l => l.Contains(words, StringComparison.Ordinal));

    public static Task Entered(this JobsHarness h, string tag) =>
        h.Lanes.Entered(tag).WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
}
