using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

/// <summary>
///     A handler takes no cancellation token — it reads <c>Current.Cancellation</c>. So a token handed to
///     a dispatch is only real if the dispatch OPENS it as the work in progress's. It did not, once: the
///     parameter was accepted, passed one frame into the generated invoker, and dropped before the handler
///     looked for it. Everything compiled, and the job processor's shutdown grace cancelled nothing.
/// </summary>
public sealed class DispatchCancellationTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Recorder());
        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_token_handed_to_a_dispatch_is_the_one_the_handler_reads()
    {
        await using var sp = Build();
        using var cts = new CancellationTokenSource();

        var seen = await sp.GetRequiredService<IDispatcher>().Query(new WhatCanCancelMe(), cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task Cancelling_that_token_cancels_the_handler_mid_call()
    {
        await using var sp = Build();
        using var cts = new CancellationTokenSource();

        var dispatch = sp.GetRequiredService<IDispatcher>().Send(new ParkUntilCancelled(), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_dispatch_given_no_token_keeps_the_one_already_in_scope()
    {
        // What makes `Dispatcher.Send(cmd)` inside a request or a job cancel with it: the dispatch adds
        // nothing of its own, so the scope the host opened is still the one the handler reads.
        await using var sp = Build();
        using var cts = new CancellationTokenSource();

        using var outer = Ambient.Enter(cts.Token);
        var seen = await sp.GetRequiredService<IDispatcher>().Query(new WhatCanCancelMe());

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task The_scope_a_dispatch_opens_ends_with_it()
    {
        // A dispatch must not leave its token behind for whatever the caller does next, which is what an
        // AsyncLocal written without a matching scope would do.
        await using var sp = Build();
        using var cts = new CancellationTokenSource();

        await sp.GetRequiredService<IDispatcher>().Query(new WhatCanCancelMe(), cts.Token);

        Assert.False(Ambient.CancellationToken.CanBeCanceled);
    }
}
