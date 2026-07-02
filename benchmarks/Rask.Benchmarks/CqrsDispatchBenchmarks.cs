using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Benchmarks;

// Measures the per-dispatch cost of the reflection-free Rask.Cqrs pipeline: a query, a command with a
// result, and a notification fanned out to two handlers. Allocations here are the registry lookup +
// the behavior-wrapping delegates; there is no reflection or MakeGenericType on the path.
[MemoryDiagnoser]
public class CqrsDispatchBenchmarks
{
    private IDispatcher _dispatcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        _dispatcher = services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    [Benchmark]
    public Task<int> Query() => _dispatcher.Query(new BenchAdd(2, 3));

    [Benchmark]
    public Task<int> SendCommand() => _dispatcher.Send(new BenchCreate("abcd"));

    [Benchmark]
    public Task Publish() => _dispatcher.Publish(new BenchPinged(1));
}

public sealed record BenchAdd(int A, int B) : IQuery<int>;

public sealed class BenchAddHandler : IQueryHandler<BenchAdd, int>
{
    public Task<int> Handle(BenchAdd query, CancellationToken cancellationToken) =>
        Task.FromResult(query.A + query.B);
}

public sealed record BenchCreate(string Name) : ICommand<int>;

public sealed class BenchCreateHandler : ICommandHandler<BenchCreate, int>
{
    public Task<int> Handle(BenchCreate command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Name.Length);
}

public sealed record BenchPinged(int Value) : INotification;

public sealed class BenchPingedA : INotificationHandler<BenchPinged>
{
    public Task Handle(BenchPinged notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class BenchPingedB : INotificationHandler<BenchPinged>
{
    public Task Handle(BenchPinged notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
