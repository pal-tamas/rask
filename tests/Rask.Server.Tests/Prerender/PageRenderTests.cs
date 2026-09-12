using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Prerender;
using Rask.Server.Tests.Endpoints;
using Rask.Server.Tests.Infrastructure;
using QueryCollection = Rask.Core.Routing.QueryCollection;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Prerender;

// The GET handler's render, driven on its own: the markup, the redirect and the status a request applies
// to its response.
public class PageRenderTests
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    [Fact]
    public async Task APage_RendersItsMarkup_WithTheRoutersStatus()
    {
        using var host = RaskTestHost.Create<PageRenderContentApp>();

        var render = await RenderAsync<PageRenderContentApp>(host, Anonymous);

        Assert.Equal(PageRenderKind.Rendered, render.Kind);
        Assert.Equal(200, render.StatusCode);
        Assert.Null(render.RedirectLocation);
        Assert.Contains("page-render-content", render.Html);
    }

    [Fact]
    public async Task APageThatNavigatesOnLoad_IsARedirect()
    {
        using var host = RaskTestHost.Create<RedirectsOnMountApp>();

        var render = await RenderAsync<RedirectsOnMountApp>(host, Anonymous);

        Assert.Equal(PageRenderKind.Redirect, render.Kind);
        Assert.Equal("/elsewhere", render.RedirectLocation);
        Assert.Equal(302, render.StatusCode);
    }

    [Fact]
    public async Task Cancelling_AbandonsTheRender()
    {
        using var host = RaskTestHost.Create<NeverSettlesApp>(
            configureServer: o => o.QuiescenceTimeout = TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RenderAsync<NeverSettlesApp>(host, Anonymous, cancel.Token));
    }

    private static async Task<PageRenderResult> RenderAsync<TApp>(
        RaskTestHost host,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
        where TApp : Component
    {
        // The root the host builds, built the same way: a page renders inside its RootErrorBoundary.
#pragma warning disable RASK014
        var session = host.Store.Create(
            sp => new RootErrorBoundary(ActivatorUtilities.CreateInstance<TApp>(sp)));
#pragma warning restore RASK014
        try
        {
            return await PageRender.RenderAsync(
                session,
                new PageRenderInput("/", QueryCollection.Empty, user, Culture: null, [], NotFoundPage: null),
                host.Services.GetRequiredService<RaskServerLimits>(),
                cancellationToken);
        }
        finally
        {
            host.Store.Remove(session.Id);
        }
    }
}

public sealed partial class PageRenderContentApp : Component
{
    protected override Component? HeadAssets => Title["page-render-content"];

    protected override Component? Render() => P["page-render-content"];
}

public sealed partial class RedirectsOnMountApp(Navigator navigator) : Component
{
    protected override Component? HeadAssets => Title["redirects-on-mount"];

    protected override void OnMount() => navigator.NavigateTo("/elsewhere");

    protected override Component? Render() => P["never served"];
}
