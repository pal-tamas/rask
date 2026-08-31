using System.Net;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Configuration;

/// <summary>
///     <c>RenderModes.ServerInteractivity = false</c> means no page ever goes live.
/// </summary>
/// <remarks>
///     <para>
///         It used to mean nothing at all: the property was read nowhere outside its own validation, so
///         turning it off with <c>Wasm</c> on was accepted and silently ignored, and turning it off with
///         <c>Wasm</c> off threw and told you to turn it back on. Every case here would have passed
///         vacuously or been unreachable.
///     </para>
///     <para>
///         The page used for most of them renders a button — deliberately. A page that needs nothing
///         live proves very little, because <c>RenderModes.Static</c> already serves those as documents.
///         What has to hold is that a page which WOULD have taken a session does not get one.
///     </para>
/// </remarks>
public class ServerInteractivityOffTests
{
    [Fact]
    public async Task APageThatWouldHaveGoneLive_IsServedAsADocument()
    {
        using var host = Host();

        var body = await host.Http.GetStringAsync("/");

        // No session id, and no runtime script to attach one.
        Assert.DoesNotContain("data-rask-root", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/rask/rask.js", body, StringComparison.Ordinal);
        Assert.Contains("press", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSessionIsRetained()
    {
        // Asserted on the counts rather than the body: a session that is merely invisible in the HTML
        // is still a DI scope and a component tree held against the cap.
        using var host = Host();

        await host.Http.GetStringAsync("/");

        Assert.Equal(0, host.Store.Count);
        Assert.Equal(0, host.Store.LiveCount);
    }

    [Fact]
    public async Task TheSocketEndpointIsNotThere()
    {
        // 404 rather than 400 or 426: an app that cannot go live should be indistinguishable from one
        // that never had a socket.
        using var host = Host();

        var response = await host.Http.GetAsync("/rask/ws");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StaticPagesDoesNotHaveToBeTurnedOnAsWell()
    {
        // The two are different statements. Static is DETECTED per page and biased towards keeping a
        // connection; this is declared, so nothing is detected and nothing can bias. Requiring both
        // would have made the weaker one load-bearing.
        using var host = RaskTestHost.Create<InteractiveApp>(
            configureServer: o =>
            {
                o.RenderModes.ServerInteractivity = false;
                o.RenderModes.Static = false;
            });

        var body = await host.Http.GetStringAsync("/");

        Assert.DoesNotContain("data-rask-root", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TurningItOffWithNoBrowserRungIsNotAContradiction()
    {
        // Plain server-side rendering for a content site. This used to throw, and the message told you
        // to turn interactivity back on — which is the opposite of what the app asked for.
        var modes = new RaskRenderModes { ServerInteractivity = false, Wasm = false };

        Assert.Null(Record.Exception(modes.Validate));
    }

    [Fact]
    public void TurningItOffWithTheBrowserRungOnIsAlsoFine()
    {
        // The edge-hosted arrangement: static HTML that hands over to WebAssembly, no socket ever.
        var modes = new RaskRenderModes { ServerInteractivity = false, Wasm = true };

        Assert.Null(Record.Exception(modes.Validate));
    }

    private static RaskTestHost Host() =>
        RaskTestHost.Create<InteractiveApp>(
            configureServer: o => o.RenderModes.ServerInteractivity = false);
}

public sealed partial class InteractiveApp : Component
{
    private int _count;

    protected override Component? HeadAssets => Title["interactive"];

    protected override Component? Render() => Div[Button.OnClick(() => _count++)["press"]];
}
