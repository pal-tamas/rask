using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authorization;
using Rask.Core.Live;
using Rask.Core.Printing;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Server.Prerender;
using QueryCollection = Rask.Core.Routing.QueryCollection;

namespace Rask.Server.Printing;

/// <summary>
/// Renders a page for a printer exactly as a first GET does — the route's authorization first, then
/// <see cref="PageRender"/> in a session of its own under the given user — and drops the session after, since
/// nothing will ever connect to it.
/// </summary>
internal sealed class ServerPrintablePages(IServiceProvider services) : IPrintablePages
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private RaskRootSelector? _selector;
    private string _pathBase = string.Empty;

    /// <summary>The pages MapRask mapped: which application answers a path, and under what path base.</summary>
    internal void Mount(RaskRootSelector selector, string pathBase)
    {
        _selector ??= selector;
        _pathBase = pathBase;
    }

    public async Task<PrintablePage> Render(RouteUrl route, ClaimsPrincipal user, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(user);
        var selector = _selector ?? throw new InvalidOperationException(
            "Pdf.From(route) prints a page of this app, and the app has mapped none: call app.MapRask<App>() first.");
        var path = route.Path;
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Pdf.From prints a page of this app, by its route; '{route}' is not one.", nameof(route));
        }

        if (!RouteResolver.TryResolve(selector.RoutesFor(path), path, out var chain, out var isNotFound) || isNotFound)
        {
            throw new InvalidOperationException($"No page of this app answers {path}, so there is nothing to print.");
        }

        await Authorize(path, chain, user).ConfigureAwait(false);

        var input = new PageRenderInput(path, Query(route.QueryString), user, Culture: null, chain, NotFoundPage: null);
        return await RenderInSession(selector.FactoryFor(path), session => Bind(session, selector, path), input, cancellation)
            .ConfigureAwait(false);
    }

    public Task<PrintablePage> Render(Component component, ClaimsPrincipal user, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(user);

        // The same wrapper a page's root gets, so the document has the app's shell, stylesheet and fonts.
#pragma warning disable RASK014 // The root has no parent render context to construct it through.
        Component Root(IServiceProvider sessionServices) =>
            new RootErrorBoundary(component) { Defaults = sessionServices.GetService<RaskDocumentDefaults>() };
#pragma warning restore RASK014

        var input = new PageRenderInput("/", QueryCollection.Empty, user, Culture: null, [], NotFoundPage: null);
        return RenderInSession(Root, session => { if (_selector is { } selector) Bind(session, selector, "/"); }, input, cancellation);
    }

    public async ValueTask<PrintableFile?> File(string path, CancellationToken cancellation)
    {
        var own = path.StartsWith(_pathBase + "/", StringComparison.Ordinal) ? path[_pathBase.Length..] : path;
        if (ScopedAsset(own) is { } scoped)
        {
            return scoped;
        }

        // The web root's file provider refuses a path that climbs out of it, and serves the app's own files: its
        // stylesheet, the kit's sheet, the fonts, the images.
        var files = services.GetService<IWebHostEnvironment>()?.WebRootFileProvider;
        if (files?.GetFileInfo(own.TrimStart('/')) is not { Exists: true, IsDirectory: false } file)
        {
            return null;
        }

        var body = new byte[file.Length];
        var stream = file.CreateReadStream();
        await using var reading = stream.ConfigureAwait(false);
        await stream.ReadExactlyAsync(body, cancellation).ConfigureAwait(false);
        return new PrintableFile(body, ContentTypes.TryGetContentType(own, out var type) ? type : "application/octet-stream");
    }

    // The component tree's own stylesheet bundle, which lives in memory rather than on disk.
    private static PrintableFile? ScopedAsset(string path)
    {
        const string Prefix = "/_rask/a/";
        if (!path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = path[Prefix.Length..];
        if (KindOf(name) is not { } kind)
        {
            return null;
        }

        var hash = name[..^ScopedAssetBundle.Extension(kind).Length];
        return ScopedAssetBundle.IsContentHash(hash) && ScopedAssetRegistry.GetByHash(hash, kind) is { } bytes
            ? new PrintableFile(bytes.Utf8.ToArray(), ScopedAssetBundle.ContentType(kind))
            : null;
    }

    private static AssetKind? KindOf(string name)
    {
        if (name.EndsWith(".css", StringComparison.Ordinal))
        {
            return AssetKind.Css;
        }

        return name.EndsWith(".js", StringComparison.Ordinal) ? AssetKind.Js : null;
    }

    private async Task Authorize(string path, IReadOnlyList<Type> chain, ClaimsPrincipal user)
    {
        var scope = services.CreateAsyncScope();
        await using var scoped = scope.ConfigureAwait(false);
        var verdict = await RouteAuthorizationGuard.Evaluate(scope.ServiceProvider, chain, user).ConfigureAwait(false);
        if (verdict.Outcome != RouteAuthorizationOutcome.Allow)
        {
            throw new UnauthorizedAccessException(
                $"The page at {path} is not open to {(user.Identity?.IsAuthenticated == true ? "this user" : "an anonymous caller")}, so it is not printed.");
        }
    }

    private async Task<PrintablePage> RenderInSession(
        Func<IServiceProvider, Component> root, Action<LiveSession> bind, PageRenderInput input, CancellationToken cancellation)
    {
        var store = services.GetRequiredService<LiveSessionStore>();
        var session = store.TryCreate(root)
            ?? throw new InvalidOperationException("The server is at its session capacity, so the page could not be rendered to print.");
        try
        {
            bind(session);
            var render = await PageRender
                .RenderAsync(session, input, services.GetRequiredService<RaskServerLimits>(), cancellation)
                .ConfigureAwait(false);
            return render switch
            {
                { Kind: PageRenderKind.Redirect } => throw new InvalidOperationException(
                    $"The page at {input.Path} sent its reader to {render.RedirectLocation} instead of rendering, so it is not printed."),
                { StatusCode: >= 400 } => throw new InvalidOperationException(
                    $"The page at {input.Path} answered {render.StatusCode} instead of rendering, so it is not printed."),
                _ => new PrintablePage(render.Html, input.Path),
            };
        }
        finally
        {
            await store.RemoveAsync(session.Id).ConfigureAwait(false);
        }
    }

    private static void Bind(LiveSession session, RaskRootSelector selector, string path) =>
        RaskEndpointExtensions.BindToApplication(session, selector, path);

    private static QueryCollection Query(string? queryString) =>
        string.IsNullOrEmpty(queryString) ? QueryCollection.Empty : new QueryCollection(QueryHelpers.ParseQuery(queryString));
}
