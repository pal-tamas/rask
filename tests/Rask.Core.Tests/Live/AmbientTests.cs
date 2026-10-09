using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Live;

// A static call — `Cache.Remember(…)`, `await Product.Create(model)` — takes neither services nor a token,
// so the handler that makes it has to leave both where it can reach them.
public sealed partial class AmbientTests : global::Rask.Core.RaskMarkup
{
    private sealed record Marker(string Name);

    [Fact]
    public async Task A_handler_reaches_the_sessions_services_and_is_cancelled_with_its_component()
    {
        IServiceProvider? seenServices = null;
        var seenToken = CancellationToken.None;
        var view = new StubComponent(() => Button.OnClick(() =>
        {
            seenServices = Ambient.Services;
            seenToken = Ambient.CancellationToken;
        })["Go"]);
        var sp = new ServiceCollection().AddSingleton(new Marker("session")).BuildServiceProvider();

        var html = view.RenderAsLiveRoot(sp);
        using var click = JsonDocument.Parse("{}");
#pragma warning disable xUnit1051 // no dispatch token on purpose: the handler's token must be its component's
        await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-click")!, click.RootElement, sp);
#pragma warning restore xUnit1051

        Assert.Equal("session", seenServices?.GetService<Marker>()?.Name);
        Assert.True(seenToken.CanBeCanceled);
        Assert.Null(Ambient.Services);   // nothing leaks out of the handler
        Assert.False(Ambient.CancellationToken.CanBeCanceled);
    }

    [Fact]
    public void A_render_reaches_its_sessions_services()
    {
        IServiceProvider? seen = null;
        var view = new StubComponent(() =>
        {
            seen = Ambient.Services;
            return Div["x"];
        });
        var sp = new ServiceCollection().AddSingleton(new Marker("render")).BuildServiceProvider();

        view.RenderAsLiveRoot(sp);

        Assert.Equal("render", seen?.GetService<Marker>()?.Name);
    }

    [Fact]
    public void A_render_sets_the_work_in_progresss_token_aside_and_puts_it_back()
    {
        using var handler = new CancellationTokenSource();
        var duringRender = handler.Token;
        var view = new StubComponent(() =>
        {
            duringRender = Ambient.CancellationToken;
            return Div["x"];
        });
        using var _ = Ambient.Enter(handler.Token);

        view.RenderAsLiveRoot();

        Assert.False(duringRender.CanBeCanceled);
        Assert.Equal(handler.Token, Ambient.CancellationToken);
    }

    [Fact]
    public void A_render_that_throws_still_puts_the_work_in_progresss_token_back()
    {
        using var handler = new CancellationTokenSource();
        var view = new StubComponent(() => throw new InvalidOperationException("render failed"));
        using var _ = Ambient.Enter(handler.Token);

        var thrown = Record.Exception(() => view.RenderAsLiveRoot());

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(handler.Token, Ambient.CancellationToken);
    }

    [Fact]
    public void Entering_the_token_already_in_scope_allocates_nothing_and_leaves_it_in_scope()
    {
        using var work = new CancellationTokenSource();
        using var outer = Ambient.Enter(work.Token);
        Ambient.Enter(work.Token).Dispose();   // warm: the first call JITs

        var before = GC.GetAllocatedBytesForCurrentThread();
        Ambient.Enter(work.Token).Dispose();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(work.Token, Ambient.CancellationToken);
    }

    [Fact]
    public void A_token_the_caller_passed_wins_over_the_ambient_one()
    {
        using var ambient = new CancellationTokenSource();
        using var passed = new CancellationTokenSource();
        using var _ = Ambient.Enter(ambient.Token);

        Assert.Equal(passed.Token, Ambient.Or(passed.Token));
        Assert.Equal(ambient.Token, Ambient.Or(CancellationToken.None));
    }
}
