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
        await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-click")!, click.RootElement, sp);

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
    public void A_token_the_caller_passed_wins_over_the_ambient_one()
    {
        using var ambient = new CancellationTokenSource();
        using var passed = new CancellationTokenSource();
        using var _ = Ambient.Enter(ambient.Token);

        Assert.Equal(passed.Token, Ambient.Or(passed.Token));
        Assert.Equal(ambient.Token, Ambient.Or(default));
    }
}
