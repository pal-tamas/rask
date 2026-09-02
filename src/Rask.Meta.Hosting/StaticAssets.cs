using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Rask.Meta.Hosting;

/// <summary>
///     Serves the framework's built client assets from Kestrel, so those requests never reach Node.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately consulted from inside the forwarder rather than registered as static-file
///         middleware, and that is not a stylistic choice. <c>StaticFileMiddleware</c> stands down when
///         routing has already matched an endpoint, and this package's fallback matches
///         <c>{*path}</c> — everything. The SPA host gets away with the middleware because its fallback
///         is <c>{*path:nonfile}</c>, leaving anything that looks like a file unmatched for the
///         middleware to pick up. That is not available here: a meta framework serves plenty of
///         dotted paths <em>dynamically</em> — a generated <c>/sitemap.xml</c>, a route ending in
///         <c>.json</c> — and excluding them from the fallback would 404 exactly those.
///     </para>
///     <para>
///         So the order is: a real file on disk wins, and everything else forwards. Nothing that is not
///         on disk is treated as static, which is what keeps a dynamic <c>/sitemap.xml</c> working.
///     </para>
/// </remarks>
internal sealed class StaticAssets : IDisposable
{
    /// <summary>
    ///     Request prefixes whose contents the framework content-hashes, and which may be cached for
    ///     ever.
    /// </summary>
    /// <remarks>
    ///     Only the three this repository has evidence for. Next writes hashed chunks under
    ///     <c>/_next/static</c>, Nuxt under <c>/_nuxt</c>, and SvelteKit names its own
    ///     <c>/_app/immutable</c>. Everything else is served <c>no-cache</c>, which is the safe
    ///     direction to be wrong in: a missed cache costs a revalidation, a wrong one strands a visitor
    ///     on a stale chunk until they clear their browser.
    /// </remarks>
    private static readonly string[] _immutablePrefixes =
        ["/_next/static/", "/_nuxt/", "/_app/immutable/"];

    private readonly FileExtensionContentTypeProvider _contentTypes = new();
    private readonly (PathString Prefix, PhysicalFileProvider Files)[] _roots;

    internal StaticAssets(MetaFramework framework, string appDirectory)
    {
        _roots = [.. framework.StaticRoots
            .Select(root => (
                Prefix: new PathString(root.RequestPath.Length == 0 ? string.Empty : root.RequestPath),
                Directory: Path.Combine(appDirectory, root.Directory.Replace('/', Path.DirectorySeparatorChar))))
            .Where(root => System.IO.Directory.Exists(root.Directory))
            .Select(root => (root.Prefix, new PhysicalFileProvider(root.Directory)))];
    }

    /// <summary>Whether any of the framework's asset directories actually exist.</summary>
    internal bool Any => _roots.Length > 0;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (_, files) in _roots)
        {
            files.Dispose();
        }
    }

    /// <summary>
    ///     Serves the request from disk if it names a built asset, and reports whether it did.
    /// </summary>
    internal async Task<bool> TryServeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        foreach (var (prefix, files) in _roots)
        {
            if (!context.Request.Path.StartsWithSegments(prefix, out var remaining))
            {
                continue;
            }

            var file = files.GetFileInfo(remaining.Value ?? string.Empty);
            if (!file.Exists || file.IsDirectory)
            {
                continue;
            }

            await SendAsync(context, file).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task SendAsync(HttpContext context, IFileInfo file)
    {
        context.Response.ContentType = _contentTypes.TryGetContentType(file.Name, out var mime)
            ? mime
            : "application/octet-stream";

        context.Response.Headers.CacheControl = IsImmutable(context.Request.Path)
            ? "public, max-age=31536000, immutable"
            : "no-cache";

        if (HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.ContentLength = file.Length;
            return;
        }

        await context.Response.SendFileAsync(
            file, context.RequestAborted).ConfigureAwait(false);
    }

    private static bool IsImmutable(PathString path) =>
        path.Value is { } value
        && Array.Exists(
            _immutablePrefixes,
            prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
