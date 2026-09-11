using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Prerender;
using Rask.Server.Tests.Endpoints;
using Rask.Server.Tests.Infrastructure;
using QueryCollection = Rask.Core.Routing.QueryCollection;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Prerender;

// The GET handler's render, driven on its own. What matters is that it returns the same verdicts the
// request used to compute inline, plus the one fact a stored copy of a page needs and a request never
// did: whether the markup depends on who it was rendered for.
public class PageRenderTests
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private static readonly ClaimsPrincipal Alice =
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], authenticationType: "test"));

    [Fact]
    public async Task APlainPage_DoesNotReadTheUser()
    {
        using var host = RaskTestHost.Create<ContentOnlyApp>();

        var render = await RenderAsync<ContentOnlyApp>(host, Anonymous);

        Assert.Equal(PageRenderKind.Rendered, render.Kind);
        Assert.False(render.ReadsUser);
        Assert.False(render.Authenticated);
        Assert.Contains("just-content", render.Html);
    }

    [Fact]
    public async Task AnAuthorizeGate_ReadsTheUser()
    {
        using var host = RaskTestHost.Create<GreetsUserApp>();

        var render = await RenderAsync<GreetsUserApp>(host, Anonymous);

        Assert.True(render.ReadsUser);
        Assert.Contains("sign in to continue", render.Html);
    }

    [Fact]
    public async Task ASignedInPrincipal_IsAuthenticated_WithoutCountingAsARead()
    {
        // The render itself reads the principal to decide Authenticated. That is the framework asking,
        // not the page, and counting it would mark every page user-dependent for every signed-in visitor.
        using var host = RaskTestHost.Create<ContentOnlyApp>();

        var render = await RenderAsync<ContentOnlyApp>(host, Alice);

        Assert.True(render.Authenticated);
        Assert.False(render.ReadsUser);
    }

    [Fact]
    public async Task AReplacedUserProvider_IsAssumedToReadTheUser()
    {
        // An app's own IUserProvider is a door the read count cannot see through. Trusting it not to have
        // been read would let one visitor's greeting be kept and shown to the next, so it is assumed read —
        // even on a page that, as it happens, never asked.
        using var host = RaskTestHost.Create<ContentOnlyApp>(
            configureServices: services => services.AddScoped<IUserProvider, FixedUserProvider>());

        var render = await RenderAsync<ContentOnlyApp>(host, Anonymous);

        Assert.True(render.ReadsUser);
    }

    [Fact]
    public async Task APageThatNavigatesOnLoad_IsARedirect()
    {
        using var host = RaskTestHost.Create<RedirectsOnMountApp>();

        var render = await RenderAsync<RedirectsOnMountApp>(host, Anonymous);

        Assert.Equal(PageRenderKind.Redirect, render.Kind);
        Assert.Equal("/elsewhere", render.RedirectLocation);
        Assert.Equal(302, render.StatusCode);
        Assert.False(render.NeedsSession);
    }

    [Fact]
    public async Task TheVerdict_IsTheOneTheRequestWouldReach()
    {
        using var interactive = RaskTestHost.Create<HandlerApp>(configureServer: o => o.RenderModes.Static = true);
        using var document = RaskTestHost.Create<ContentOnlyApp>(configureServer: o => o.RenderModes.Static = true);

        var handler = await RenderAsync<HandlerApp>(interactive, Anonymous);
        var content = await RenderAsync<ContentOnlyApp>(document, Anonymous);

        Assert.True(handler.NeedsSession);
        Assert.False(content.NeedsSession);
        Assert.Equal(200, content.StatusCode);
        Assert.False(content.Faulted);
        Assert.False(content.TimedOut);
    }

    [Fact]
    public async Task Cancelling_AbandonsTheRender()
    {
        using var host = RaskTestHost.Create<NeverSettlesStaticApp>(
            configureServer: o => o.RenderModes.QuiescenceTimeout = TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RenderAsync<NeverSettlesStaticApp>(host, Anonymous, cancel.Token));
    }

    private static async Task<PageRenderResult> RenderAsync<TApp>(
        RaskTestHost host,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
        where TApp : Component
    {
        // The root the host builds, built the same way: a page renders inside its RootErrorBoundary.
#pragma warning disable RASK014
        var session = host.Store.CreateDetached(
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
            await host.Store.DiscardAsync(session);
        }
    }

    /// <summary>An app's own provider, standing in for anything that is not the framework's.</summary>
    private sealed class FixedUserProvider : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = new(new ClaimsIdentity());

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}

public sealed partial class GreetsUserApp : Component
{
    protected override Component? HeadAssets => Title["greets-user"];

    protected override Component? Render() =>
        Div[
            Authorize
                .Authorized(user => P[$"hello {user.Identity!.Name}"])
                .NotAuthorized(P["sign in to continue"])
        ];
}

public sealed partial class RedirectsOnMountApp(Navigator navigator) : Component
{
    protected override Component? HeadAssets => Title["redirects-on-mount"];

    protected override void OnMount() => navigator.NavigateTo("/elsewhere");

    protected override Component? Render() => P["never served"];
}
