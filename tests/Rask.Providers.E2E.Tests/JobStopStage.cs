using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Rask.Providers.E2E.Tests;

// ── The jobs the stop, limit and lane tests run on a real server. Each one says how it ended in the stage's notes:
//    "{tag}:done", "{tag}:stopped:{why}" (Jobs.Stopped as its handler read it) or "{tag}:returned:{why}". ─────────────────

/// <summary>Parks until released, passing <c>Current.Cancellation</c> on — the well-behaved job.</summary>
public sealed record HoldJob(string Tag) : IJob;

public sealed class HoldJobHandler(JobStopStage stage) : ICommandHandler<HoldJob>
{
    public async Task Handle(HoldJob command)
    {
        stage.Enter(command.Tag);
        try
        {
            await stage.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
            stage.Note($"{command.Tag}:done");
        }
        catch (OperationCanceledException)
        {
            stage.Note($"{command.Tag}:stopped:{Jobs.Stopped?.ToString() ?? "none"}");
            throw;
        }
    }
}

/// <summary>Ignores its token: it returns only when released, so a stop leaves its row stopped but not yet ended.</summary>
public sealed record DeafJob(string Tag) : IJob;

public sealed class DeafJobHandler(JobStopStage stage) : ICommandHandler<DeafJob>
{
    public async Task Handle(DeafJob command)
    {
        stage.Enter(command.Tag);
        await stage.Released(command.Tag).ConfigureAwait(false);
        stage.Note($"{command.Tag}:returned:{Jobs.Stopped?.ToString() ?? "none"}");
    }
}

/// <summary>Reports progress each time the test pulses it, until it is stopped.</summary>
public sealed record PulseJob(string Tag) : IJob;

public sealed class PulseJobHandler(JobStopStage stage) : ICommandHandler<PulseJob>
{
    public async Task Handle(PulseJob command)
    {
        stage.Enter(command.Tag);
        try
        {
            while (true)
            {
                var asked = await stage.NextPulse(command.Tag, Current.Cancellation).ConfigureAwait(false);
                Jobs.Report($"{command.Tag} got on");
                asked.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            stage.Note($"{command.Tag}:stopped:{Jobs.Stopped?.ToString() ?? "none"}");
            throw;
        }
    }
}

/// <summary>Runs at once and notes that it did.</summary>
public sealed record NoteJob(string Tag) : IJob;

public sealed class NoteJobHandler(JobStopStage stage) : ICommandHandler<NoteJob>
{
    public Task Handle(NoteJob command)
    {
        stage.Note($"{command.Tag}:done");
        return Task.CompletedTask;
    }
}

/// <summary>One test's levers on its jobs: when each entered its handler, when it may leave, its pulses, and what it noted.</summary>
public sealed class JobStopStage
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _entered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _released = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Channel<TaskCompletionSource>> _pulses = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _notes = new();

    public IReadOnlyCollection<string> Notes => [.. _notes];

    public ConcurrentQueue<string> Logs { get; } = new();

    public void Enter(string tag) => Latch(_entered, tag).TrySetResult();

    public bool HasEntered(string tag) => Latch(_entered, tag).Task.IsCompleted;

    public Task Entered(string tag) => Latch(_entered, tag).Task.WaitAsync(JobStopWait.Ceiling, TestContext.Current.CancellationToken);

    public Task Released(string tag) => Latch(_released, tag).Task;

    public void Release(string tag) => Latch(_released, tag).TrySetResult();

    /// <summary>Lets every job go, so stopping a processor never waits on a job a failed test left parked.</summary>
    public void ReleaseAll()
    {
        foreach (var latch in _released.Values)
        {
            latch.TrySetResult();
        }
    }

    public void Note(string note) => _notes.Enqueue(note);

    /// <summary>Makes the <see cref="PulseJob" /> of <paramref name="tag" /> report once, and waits until it has.</summary>
    public async Task Pulse(string tag)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Pulses(tag).Writer.WriteAsync(done).ConfigureAwait(false);
        await done.Task.WaitAsync(JobStopWait.Ceiling).ConfigureAwait(false);
    }

    public ValueTask<TaskCompletionSource> NextPulse(string tag, CancellationToken cancellationToken) =>
        Pulses(tag).Reader.ReadAsync(cancellationToken);

    private Channel<TaskCompletionSource> Pulses(string tag) =>
        _pulses.GetOrAdd(tag, _ => Channel.CreateUnbounded<TaskCompletionSource>());

    private static TaskCompletionSource Latch(ConcurrentDictionary<string, TaskCompletionSource> latches, string tag) =>
        latches.GetOrAdd(tag, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>How long a test waits for something that must happen, and the wait itself, which names what it waited for.</summary>
internal static class JobStopWait
{
    // Generous: a shared CI runner with two database servers in containers is slow, and a hang is the only thing this catches.
    internal static TimeSpan Ceiling => TimeSpan.FromSeconds(60);

    internal static async Task Until(
        Func<Task<bool>> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var deadline = DateTime.UtcNow + Ceiling;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"`{expression}` was still false after {Ceiling}.");
            }

            await Task.Delay(25);
        }
    }
}

/// <summary>Keeps every formatted log line of one host, for asserting on what the processor said.</summary>
internal sealed class JobStopLogs(ConcurrentQueue<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(sink);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(formatter(state, exception));
    }
}

/// <summary>Counts the queries read through the jobs' contexts.</summary>
public sealed class JobStopReads : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    private int _reads;

    public int Count => Volatile.Read(ref _reads);

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command,
        Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
