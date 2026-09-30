using System.Collections.Concurrent;

namespace Rask.Cqrs.Tests;

// A shared, per-instance sink so tests can assert what ran and in what order without static state.
public sealed class Recorder
{
    public ConcurrentQueue<string> Log { get; } = new();

    public void Add(string entry) => Log.Enqueue(entry);

    public IReadOnlyList<string> Entries => Log.ToArray();
}

// ---- Query ----
public sealed record Add(int A, int B) : IQuery<int>;

public sealed class AddHandler : IQueryHandler<Add, int>
{
    public Task<int> Handle(Add query) =>
        Task.FromResult(query.A + query.B);
}

// ---- Void command ----
public sealed record Poke(string Name) : ICommand;

public sealed class PokeHandler(Recorder recorder) : ICommandHandler<Poke>
{
    public Task Handle(Poke command)
    {
        recorder.Add($"poke:{command.Name}");
        return Task.CompletedTask;
    }
}

// ---- Command with result ----
public sealed record CreateThing(string Name) : ICommand<int>;

public sealed class CreateThingHandler : ICommandHandler<CreateThing, int>
{
    public Task<int> Handle(CreateThing command) =>
        Task.FromResult(command.Name.Length);
}

// ---- Event with two handlers ----
public sealed record Pinged(string Message) : IEvent;

public sealed class PingedHandlerA(Recorder recorder) : IEventHandler<Pinged>
{
    public Task Handle(Pinged e)
    {
        recorder.Add($"A:{e.Message}");
        return Task.CompletedTask;
    }
}

public sealed class PingedHandlerB(Recorder recorder) : IEventHandler<Pinged>
{
    public Task Handle(Pinged e)
    {
        recorder.Add($"B:{e.Message}");
        return Task.CompletedTask;
    }
}

// ---- Event nobody handles ----
public sealed record Unheard : IEvent;

// ---- An open-generic behavior that records entry/exit around every request ----
public sealed class TracingBehavior<TRequest, TResult>(Recorder recorder) : IPipelineBehavior<TRequest, TResult>
{
    public async Task<TResult> Handle(TRequest request, RequestHandler<TResult> next)
    {
        recorder.Add($"trace-in:{typeof(TRequest).Name}");
        var result = await next();
        recorder.Add($"trace-out:{typeof(TRequest).Name}");
        return result;
    }
}

// ---- A second behavior to prove ordering ----
public sealed class SecondBehavior<TRequest, TResult>(Recorder recorder) : IPipelineBehavior<TRequest, TResult>
{
    public async Task<TResult> Handle(TRequest request, RequestHandler<TResult> next)
    {
        recorder.Add("second-in");
        var result = await next();
        recorder.Add("second-out");
        return result;
    }
}

// ---- A short-circuiting closed behavior for Add: returns 999 without calling next ----
public sealed class ShortCircuitAdd(Recorder recorder) : IPipelineBehavior<Add, int>
{
    public Task<int> Handle(Add request, RequestHandler<int> next)
    {
        recorder.Add("short-circuit");
        return Task.FromResult(999);
    }
}

// ---- A request with no registered handler (to prove the clear runtime error) ----
public sealed record Orphan : IQuery<int>;

// ---- An event whose handlers fail, for the publish failure-mode tests. One succeeds and two
// throw; the throwing handlers are async (record, yield, then throw) so under WhenAll every handler's
// task is started before any of them faults. ----
public sealed record Grumble(string Tag) : IEvent;

public sealed class GrumbleOk(Recorder recorder) : IEventHandler<Grumble>
{
    public Task Handle(Grumble e)
    {
        recorder.Add($"ok:{e.Tag}");
        return Task.CompletedTask;
    }
}

public sealed class GrumbleBoomOne(Recorder recorder) : IEventHandler<Grumble>
{
    public async Task Handle(Grumble e)
    {
        recorder.Add($"boom1:{e.Tag}");
        await Task.Yield();
        throw new InvalidOperationException("boom-1");
    }
}

public sealed class GrumbleBoomTwo(Recorder recorder) : IEventHandler<Grumble>
{
    public async Task Handle(Grumble e)
    {
        recorder.Add($"boom2:{e.Tag}");
        await Task.Yield();
        throw new InvalidOperationException("boom-2");
    }
}

// ---- The token a dispatch is given, as the handler sees it. A handler takes no token: it reads
// Current.Cancellation, so this is the only way to observe what the dispatch actually opened. ----
public sealed record WhatCanCancelMe : IQuery<CancellationToken>;

public sealed class WhatCanCancelMeHandler : IQueryHandler<WhatCanCancelMe, CancellationToken>
{
    public Task<CancellationToken> Handle(WhatCanCancelMe query) =>
        Task.FromResult(Current.Cancellation);
}

public sealed record ParkUntilCancelled : ICommand;

public sealed class ParkUntilCancelledHandler : ICommandHandler<ParkUntilCancelled>
{
    public Task Handle(ParkUntilCancelled command) =>
        Task.Delay(Timeout.Infinite, Current.Cancellation);
}
