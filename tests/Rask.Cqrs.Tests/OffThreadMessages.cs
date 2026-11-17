namespace Rask.Cqrs.Tests;

// Registered only in the worker's container, so a handler can say which side ran it.
public sealed class WorkerSide;

// Held by a test and handed to a handler that must not finish until the test says so.
public sealed class Gate
{
    public ManualResetEventSlim Release { get; } = new();

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int? Thread { get; set; }

    public SynchronizationContext? Context { get; set; }
}

[OffThread]
public sealed record WhereAmI(string Label) : IQuery<string>;

public sealed class WhereAmIHandler(WorkerSide? side = null) : IQueryHandler<WhereAmI, string>
{
    public Task<string> Handle(WhereAmI query) =>
        Task.FromResult($"{query.Label} on the {(side is null ? "page" : "worker")} in {System.Globalization.CultureInfo.CurrentCulture.Name}");
}

[OffThread]
public sealed record SumBytes(byte[] Bytes) : IQuery<long>;

public sealed class SumBytesHandler : IQueryHandler<SumBytes, long>
{
    public Task<long> Handle(SumBytes query) => Task.FromResult(query.Bytes.Sum(static b => (long)b));
}

[OffThread]
public sealed record Reversed(byte[] Bytes) : IQuery<byte[]>;

public sealed class ReversedHandler : IQueryHandler<Reversed, byte[]>
{
    public Task<byte[]> Handle(Reversed query) => Task.FromResult(query.Bytes.Reverse().ToArray());
}

[OffThread]
public sealed record Refuse(string Field) : ICommand;

public sealed class RefuseHandler : ICommandHandler<Refuse>
{
    public Task Handle(Refuse command) =>
        throw new RaskValidationException([new FieldFailure("Not like that.", [command.Field])]);
}

[OffThread]
public sealed record Break(string Why) : ICommand<int>;

public sealed class BreakHandler : ICommandHandler<Break, int>
{
    public Task<int> Handle(Break command) => throw new InvalidOperationException(command.Why);
}

// Waits on Current.Cancellation, as a handler that yields can.
[OffThread]
public sealed record WaitPolitely(int Id) : IQuery<int>;

public sealed class WaitPolitelyHandler(Gate gate) : IQueryHandler<WaitPolitely, int>
{
    public async Task<int> Handle(WaitPolitely query)
    {
        gate.Entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, Current.Cancellation);
            return query.Id;
        }
        catch (OperationCanceledException)
        {
            gate.Cancelled.TrySetResult(true);
            throw;
        }
    }
}

// Never yields, as a synchronous Xlsx.Read does.
[OffThread]
public sealed record Grind(int Id) : IQuery<int>;

public sealed class GrindHandler(Gate gate) : IQueryHandler<Grind, int>
{
    public Task<int> Handle(Grind query)
    {
        gate.Thread = Environment.CurrentManagedThreadId;
        gate.Context = SynchronizationContext.Current;
        gate.Entered.TrySetResult();
        gate.Release.Wait();
        return Task.FromResult(query.Id);
    }
}
