using System.Net;
using Rask.Core;
using Rask.Core.Http;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// The framework answers 404 for a path that fell through and 500 for a render that faulted. Neither
// helps /products/9999: it matches a real route, renders an ordinary "no such product" page, and
// tells every cache and crawler it is fine. Only the page knows.
public class PageResponseTests
{
    [Fact]
    public async Task SetStatus_FromOnMount_ShapesTheResponse()
    {
        using var host = RaskTestHost.Create<StatusApp>();

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // The page still renders — a soft 404 is still a page, not an empty body.
        Assert.Contains("no-such-product", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetStatus_LosesToAFaultedRender()
    {
        // A page that threw does not get to claim it succeeded, and the error document is what is
        // actually being served.
        using var host = RaskTestHost.Create<StatusThenThrowApp>();

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task NavigateTo_DuringTheInitialRender_AnswersARealRedirect()
    {
        // The same NavigateTo a handler would call. On the first render the host turns it into a
        // 302 — one response instead of a whole page the client immediately navigates away from,
        // and one a crawler and a cache both understand.
        using var host = RaskTestHost.Create<RedirectApp>();

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/somewhere-else", response.Headers.Location?.ToString());
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NavigateTo_DuringTheInitialRender_KeepsNoSessionBehind()
    {
        // Nothing will ever connect to a page that redirected, so holding a DI scope and a
        // component tree for it is pure waste.
        using var host = RaskTestHost.Create<RedirectApp>();

        await host.Http.GetAsync("/");

        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task ARedirect_IsNeverCacheable()
    {
        // One computed from runtime state — a flag, a tenant, an experiment — that a browser
        // pinned would be unrecoverable without changing the URL.
        using var host = RaskTestHost.Create<RedirectApp>();

        var response = await host.Http.GetAsync("/");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public void SetStatus_OutsideTheInitialRender_Throws()
    {
        var page = new Rask.Server.Http.ServerPageResponse();

        var ex = Assert.Throws<InvalidOperationException>(() => page.SetStatus(404));

        // The message has to say why, because the failure mode it replaces — a status silently
        // dropped because the response was already sent — is invisible.
        Assert.Contains("initial server render", ex.Message);
    }

    [Fact]
    public void SetStatus_OutsideTheHttpRange_Throws()
    {
        var page = new Rask.Server.Http.ServerPageResponse
        {
            Phase = Rask.Server.Http.PageResponsePhase.Initial
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => page.SetStatus(100));
    }
}

public sealed partial class StatusApp(IPageResponse response) : Component
{
    protected override Component? HeadAssets => Title["status"];

    protected override void OnMount() => response.SetStatus(404);

    protected override Component? Render() => Div["no-such-product"];
}

public sealed partial class StatusThenThrowApp(IPageResponse response) : Component
{
    protected override Component? HeadAssets => Title["status-throw"];

    protected override void OnMount() => response.SetStatus(200);

    protected override Component? Render() => throw new InvalidOperationException("boom");
}

public sealed partial class RedirectApp(Navigator navigator) : Component
{
    protected override Component? HeadAssets => Title["redirect"];

    protected override void OnMount() => navigator.NavigateTo("/somewhere-else");

    protected override Component? Render() => Div["should-not-be-served"];
}

