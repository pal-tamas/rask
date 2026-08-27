using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Hosting.Shared;

namespace Rask.Wasm.Hosting;

/// <summary>
///     Serves a published WASM bundle from an ASP.NET Core host — the runtime, the scoped assets, and the
///     SPA fallback that keeps client-side routes working on refresh.
/// </summary>
public static class RaskWasmEndpointExtensions
{
    // Matches Blazor/WASM SDK fingerprinted asset names: <stem>.<10+ hex/alphanumeric>.<ext>.
    // The hash segment is at least 10 chars to avoid false positives on filenames like
    // System.IO.Pipelines.wasm (where "Pipelines" is alphanumeric but isn't a content hash).
    // When <WasmFingerprintAssets>true</WasmFingerprintAssets> is enabled on the consumer's
    // WASM project, the SDK emits names like dotnet.7a8b9c2d3e4f5061.js and this regex hits.
    // The default SDK output (filenames like dotnet.js, Rask.Core.wasm) doesn't fingerprint
    // and falls through to no-cache.
    private static readonly Regex _fingerprintRegex = new(
        @"\.[0-9a-z]{10,}\.[^.]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Resolves the real MIME of an asset by extension. Needed because PrecompressedFileMiddleware
    // rewrites a request for foo.css to its on-disk foo.css.br sibling, and UseStaticFiles then
    // keys the content type off the .br extension — unknown, so it lands on DefaultContentType
    // (application/octet-stream). Browsers reject a stylesheet/script served as octet-stream, so we
    // re-derive the type from the underlying name (suffix stripped) in OnPrepareResponse below.
    private static readonly FileExtensionContentTypeProvider _contentTypes = new();

    /// <summary>
    ///     Serves a published Rask WASM AppBundle as a SPA: <c>UseDefaultFiles</c> +
    ///     <c>UseStaticFiles</c> with the right MIME types for <c>.wasm</c>/<c>.js</c>, plus a
    ///     <c>MapFallback</c> to <c>index.html</c> so client-side routes resolve. The bundle's
    ///     own <c>index.html</c> (shipped from <c>Rask.Wasm/Browser/</c> via the framework
    ///     targets) is the real entry point — the WASM runtime takes over once it boots.
    ///     <para>
    ///         Map any <c>/api/*</c> minimal-API endpoints <b>before</b> calling this — the SPA
    ///         fallback would otherwise shadow them.
    ///     </para>
    ///     <para>
    ///         Bundle path resolution order: explicit <paramref name="bundlePath" /> →
    ///         <c>[assembly: AssemblyMetadata("Rask.WasmAppBundleDir", "...")]</c> baked into the
    ///         entry assembly by <c>build/Rask.Wasm.Hosting.targets</c> (auto-imported when the
    ///         host has a <c>&lt;ProjectReference&gt;</c> to a project that sets
    ///         <c>&lt;WasmGenerateAppBundle&gt;true&lt;/WasmGenerateAppBundle&gt;</c>) → 503
    ///         fallback with a "no bundle configured" message.
    ///     </para>
    /// </summary>
    /// <summary>
    ///     Generic form of <see cref="UseRask(IEndpointRouteBuilder, string?, string)" /> that
    ///     additionally touches <typeparamref name="TApp" /> so the runtime loads the
    ///     consumer's component assembly. The assembly's source-generator-emitted
    ///     <c>__RaskScopedCssRegistration</c> / <c>__RaskScopedJsRegistration</c> classes
    ///     carry <c>[ModuleInitializer]</c>, which only fires on assembly load. Without
    ///     that touch the host process never realises the assembly exists (only
    ///     <c>Rask.Wasm.Hosting</c> is referenced from <c>Program</c> in a typical
    ///     WASM-host project), <see cref="Rask.Core.ScopedAssets.ScopedAssetRegistry" />
    ///     stays empty, and every browser-side <c>GET /_rask/a/{hash}.{ext}</c> returns 404
    ///     because the hashes the browser computed (from the in-WASM-runtime registry) are
    ///     unknown on the host side.
    ///     <para>
    ///         Consumers call this from <c>Program.cs</c> as
    ///         <c>app.UseRask&lt;App&gt;()</c>, mirroring the long-standing
    ///         <c>Rask.Server</c> shape.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     In an app that references <b>both</b> hosts, prefer <see cref="UseRaskWasmHost{TApp}" />:
    ///     <c>Rask.Server</c> declares a <c>UseRask&lt;TApp&gt;</c> too, whose second parameter is a
    ///     route <em>pattern</em> where this one takes a bundle <em>path</em>. Neither the compiler nor
    ///     the reader can tell those apart at the call site.
    /// </remarks>
    public static IEndpointRouteBuilder UseRask<TApp>(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null,
        string pathBase = "")
    {
        // typeof(TApp) is the load trigger — references the type token, which forces the
        // runtime to resolve and JIT-init the defining assembly, which runs every
        // [ModuleInitializer] in that assembly. Discarded to make the intent obvious.
        _ = typeof(TApp);
        return endpoints.UseRask(bundlePath, pathBase);
    }

    /// <summary>
    ///     <see cref="UseRask{TApp}(IEndpointRouteBuilder, string?, string)" /> under a name only this
    ///     package defines — for an app that references both hosts.
    ///     <para>
    ///         A wasm-hosted app that mounts the operator dashboard calls both: <c>UseRask&lt;Shell&gt;</c>
    ///         from <c>Rask.Server</c> to serve the server-rendered chain under its prefix, and this to
    ///         serve the SPA everywhere else. Written as two <c>UseRask</c> calls they differ only in
    ///         argument types, and one of them silently means a bundle path.
    ///     </para>
    /// </summary>
    /// <typeparam name="TApp">The client's root component, touched to run its module initializers.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="bundlePath">Where the published bundle lives. Defaults to the conventional location.</param>
    /// <param name="pathBase">Prefix to serve the bundle under. Empty serves it at the root.</param>
    public static IEndpointRouteBuilder UseRaskWasmHost<TApp>(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null,
        string pathBase = "") =>
        endpoints.UseRask<TApp>(bundlePath, pathBase);

    /// <summary>
    ///     Non-generic <see cref="UseRaskWasmHost{TApp}" />: serves the bundle without touching the
    ///     client assembly. Prefer the generic form — see
    ///     <see cref="UseRask(IEndpointRouteBuilder, string?, string)" /> for why.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="bundlePath">Where the published bundle lives. Defaults to the conventional location.</param>
    /// <param name="pathBase">Prefix to serve the bundle under. Empty serves it at the root.</param>
    public static IEndpointRouteBuilder UseRaskWasmHost(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null,
        string pathBase = "") =>
        endpoints.UseRask(bundlePath, pathBase);

    /// <summary>
    ///     Serves a published WASM bundle: the runtime files, the scoped CSS/JS assets, and a fallback so
    ///     client-side routes survive a refresh or a deep link.
    /// </summary>
    /// <remarks>
    ///     Prefer the <see cref="UseRask{TApp}(IEndpointRouteBuilder, string?, string)" /> overload. This
    ///     one does not touch the component assembly, so unless something else has already loaded it, its
    ///     scoped-asset registrations never run and every <c>/_rask/a/{hash}</c> request 404s.
    ///     <para>
    ///         A non-empty <paramref name="pathBase" /> scopes every endpoint under that prefix, which is
    ///         what lets two bundles live side by side in one host.
    ///     </para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="bundlePath">Where the published bundle lives. Defaults to the conventional location.</param>
    /// <param name="pathBase">Prefix to serve the bundle under. Empty serves it at the root.</param>
    /// <param name="serveIndexFallback">
    ///     Map the SPA fallback to <c>index.html</c>. Pass <c>false</c> when something else owns the
    ///     catch-all — see <see cref="UseRaskWasmAssets" />.
    /// </param>
    /// <returns><paramref name="endpoints" />, for chaining.</returns>
    public static IEndpointRouteBuilder UseRask(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null,
        string pathBase = "",
        bool serveIndexFallback = true)
    {
        // Normalize once and stash on the static accessor so HeadAssetRegistry's URL
        // emission (and any future server-side URL emission running in this process)
        // picks up the same prefix. A non-empty value also scopes every endpoint and
        // static-file mapping below under the prefix so two WASM AppBundles can live
        // side-by-side in one host process.
        var pathBaseNormalized = RaskPath.Normalize(pathBase);
        LiveOptions.PathBase = pathBaseNormalized;

        var resolved = bundlePath ?? WasmAppBundle.ResolveFromAssembly(Assembly.GetEntryAssembly());
        var bundleDir = string.IsNullOrEmpty(resolved) || !Directory.Exists(resolved) ? null : resolved;

        // Publish the bundle to Core BEFORE anything maps the shared /_rask/a/{hash} route. Rask.Server
        // reads it from there on a registry miss, so in an app running both hosts (wasm-hosted with the
        // operator dashboard) either handler resolves the same baked file — which is what makes it safe
        // for only one of them to own the route, in whichever order the two UseRask calls appear.
        ScopedAssetBundle.BakedDirectory = bundleDir;

        // Dev bundle: serve the client's BUILD output (via its static-web-assets manifest) instead of
        // the published one. Gated on all three of Development, hot reload being supported in this
        // process, and the manifest existing — so a published deployment, a Release run, and a host
        // built without a WASM reference all take the untouched path below.
        //
        // This is what makes WASM hot reload possible at all: a published bundle is trimmed, and
        // trimming folds MetadataUpdater.IsSupported to false, so the session in the browser never
        // registers for hot reload no matter what the host serves.
        //
        // Decided BEFORE the missing-bundle guard, and deliberately: turning the dev bundle on skips
        // the nested publish, so there is usually no publish directory at all. Checking the publish
        // path first would 503 exactly the setup this feature creates — which is precisely what
        // happened, and what the WASM watch E2E caught.
        var devManifest = WasmAppBundle.ResolveDevManifest(Assembly.GetEntryAssembly());
        var dev = MetadataUpdater.IsSupported
                  && endpoints.ServiceProvider.GetService<IHostEnvironment>()?.IsDevelopment() == true
                  && !string.IsNullOrEmpty(devManifest)
                  && File.Exists(devManifest);

        // Scoped asset endpoint. Registered before the static-file middleware so a /_rask/a/{hash}.{ext}
        // URL is served from the in-process ScopedAssetRegistry (populated by module initializers from
        // the referenced WASM assembly as soon as it's loaded into this host). On a registry miss it
        // falls back to the baked file in the published bundle: this host's registry can be a strict
        // subset of the in-WASM-runtime set, so its hash for the single concatenated CSS/JS bundle won't
        // match the browser's request — and because routing matched this endpoint, UseStaticFiles is
        // skipped and can't serve the baked file itself. The baked copy is authoritative.
        RaskAssetEndpoint.MapRaskAssets(endpoints, pathBaseNormalized);

        // `!dev`: with the dev bundle on there is no published directory to find — skipping the nested
        // publish is the point — so the missing-bundle guard must not fire.
        if (!dev && (string.IsNullOrEmpty(resolved) || !Directory.Exists(resolved)))
        {
            var reason = string.IsNullOrEmpty(resolved)
                ? "no bundle configured (add a <ProjectReference> to a project that sets <WasmGenerateAppBundle>true</WasmGenerateAppBundle>, with ReferenceOutputAssembly=\"false\" SkipGetTargetFrameworkProperties=\"true\", or pass bundlePath explicitly)"
                : $"bundle not found at {resolved} (run `dotnet publish` on the WASM project)";
            Console.Error.WriteLine($"Rask.Wasm.Hosting: {reason}.");

            StaticSpaFiles.MapCatchAll(endpoints, pathBaseNormalized, async ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsync($"Rask WASM AppBundle unavailable: {reason}.");
            });

            return endpoints;
        }

        if (endpoints is not IApplicationBuilder app)
        {
            throw new InvalidOperationException(
                "UseRask must be called on an IEndpointRouteBuilder that is also an " +
                "IApplicationBuilder (e.g. WebApplication).");
        }

        // Past the guard above, `resolved` is non-null on the published path; on the dev path it may be
        // absent entirely, and nothing below reads it.
        IFileProvider fileProvider = dev
            ? new StaticWebAssetsManifestFileProvider(devManifest!)
            : new PhysicalFileProvider(resolved!);

        if (dev)
        {
            Console.WriteLine($"Rask.Wasm.Hosting: serving the WASM build output (hot reload) from {devManifest}");
        }

        // Precompressed siblings first: if the WASM publish step emitted .br/.gz next to
        // each asset (set <CompressionEnabled>true</CompressionEnabled> on the WASM
        // project), serve those directly with the matching Content-Encoding header — zero
        // request-time CPU and CDN-cacheable. Falls through to UseStaticFiles + the
        // runtime UseResponseCompression below when no sibling exists.
        // Both compression paths are skipped in dev. `dotnet watch`'s browser-refresh middleware injects
        // its script tag by rewriting the HTML response body, and it cannot rewrite an encoded one — and
        // that script is the single condition Mono's in-browser delta applier self-arms on. A gzipped
        // shell therefore means no hot reload, silently.
        if (!dev)
        {
            app.UseMiddleware<PrecompressedFileMiddleware>(fileProvider);
        }

        // If the host called services.AddRask() (which registers brotli/gzip providers and
        // adds application/wasm + application/octet-stream to the compressible MIME set),
        // wire UseResponseCompression ahead of UseStaticFiles so .wasm/.dll/.js/.json bodies
        // ship compressed. Skipped silently when not registered — the host still works,
        // just without compression. With the precompressed-sibling middleware ahead, this
        // only fires for files without a baked sibling (e.g. index.html if not pre-encoded).
        if (!dev && app.ApplicationServices.GetService<IResponseCompressionProvider>() is not null)
        {
            app.UseResponseCompression();
        }

        // Only when this host owns its root. UseDefaultFiles rewrites a request for "/" to
        // "/index.html", so leaving it on in assets-only mode hands the bundle's SPA shell to every
        // visitor arriving at the home page of the app that is supposed to be rendering it — the exact
        // shadowing the assets-only form exists to avoid, reintroduced one layer down. The MapFallback
        // is the obvious half of that; this rewrite is the half that hides, because it affects one
        // path and every other route keeps working.
        if (serveIndexFallback)
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = fileProvider,
                RequestPath = pathBaseNormalized,
                DefaultFileNames = new[] { "index.html" }
            });
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = pathBaseNormalized,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
            OnPrepareResponse = ctx =>
            {
                // The precompressed middleware may have rewritten the path to a .br/.gz
                // sibling — strip the encoding suffix so the MIME/cache classification
                // reflects the underlying asset (foo.wasm.br is still "application/wasm").
                var name = StaticSpaFiles.UnderlyingFileName(ctx.File.Name);

                var ext = Path.GetExtension(name);
                if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.ContentType = "application/wasm";
                }
                else if (ext.Equals(".js", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.ContentType = "application/javascript";
                }
                else if (_contentTypes.TryGetContentType(name, out var mime))
                {
                    // Any other known type (.css/.json/.svg/...). For a directly served file
                    // UseStaticFiles already set this; for a precompressed .br/.gz sibling it had
                    // fallen back to octet-stream — this restores it. Unknown extensions (.bin etc.)
                    // aren't in the provider, so they stay application/octet-stream as before.
                    ctx.Context.Response.ContentType = mime;
                }

                // Cache classification:
                //   - Fingerprinted _framework/ assets (filename contains an embedded content
                //     hash, e.g. dotnet.7a8b9c2d.js) — safe to mark immutable: the URL changes
                //     when the bytes change. Browsers serve from disk cache with zero round
                //     trips for the year.
                //   - Everything else (index.html, main.js, rask.wasm.js, unfingerprinted
                //     _framework/*.wasm in the default WASM SDK output) — `no-cache` so a
                //     deploy that rewrites the same filename is picked up immediately. ETag +
                //     Last-Modified still give the 304 fast path on unchanged bodies, but
                //     without `no-cache` browsers (especially for ES module imports) reuse
                //     disk-cached copies across page loads without revalidating.
                ctx.Context.Response.Headers.CacheControl = IsFingerprintedAsset(name)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            }
        });

        // In dev the shell comes from the manifest, not from disk next to the bundle: the build output's
        // wwwroot/ holds only _framework/, and /index.html maps to a placeholder-filled shell under obj/
        // whose import map and preload hints carry the build fingerprints. The raw index.html in the
        // source tree still has those placeholders empty and cannot boot the runtime.
        //
        // SendFileAsync is kept on both paths. It was worth checking, since `dotnet watch`'s
        // browser-refresh middleware injects its script by rewriting the response body and the delta
        // applier arms on nothing else — but it rewrites the SendFileAsync path too, verified against a
        // running host on both the published and the build bundle.
        var indexPath = dev
            ? fileProvider.GetFileInfo("index.html").PhysicalPath
              ?? throw new InvalidOperationException(
                  $"The WASM dev bundle manifest has no index.html entry ({devManifest}). Rebuild the "
                  + "client project, or run without -p:RaskWasmDevBundle=true to serve the published bundle.")
            : Path.Combine(resolved!, "index.html");

        // An app whose pages are rendered by Rask.Server wants the bundle's ASSETS and none of its
        // routing: the server owns the catch-all, and a SPA fallback here would shadow every page it
        // renders. That is the arrangement a browser takeover needs — the visitor is served real HTML
        // by the server, and the bundle sits there for the page to boot when it decides to.
        if (serveIndexFallback)
        {
            StaticSpaFiles.MapCatchAll(endpoints, pathBaseNormalized, async ctx =>
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                await ctx.Response.SendFileAsync(indexPath);
            });
        }

        return endpoints;
    }

    /// <summary>
    ///     Serves a published WASM bundle's assets from an app whose pages something else renders —
    ///     no SPA fallback, no catch-all.
    /// </summary>
    /// <remarks>
    ///     For a server-rendered app that also ships a browser bundle. <c>Rask.Server</c> owns the
    ///     catch-all and answers every page; this only makes <c>_framework/</c> and the rest of the
    ///     bundle reachable, so a page can boot the runtime when it chooses to.
    ///     <para>
    ///         <b>Call this before <c>UseRouting()</c>.</b> Routing selects an endpoint before the
    ///         static-file middleware runs, and that middleware steps aside when one is already
    ///         selected — so mapping the bundle afterwards lets the server's catch-all answer
    ///         <c>/_framework/*.wasm</c> with <c>text/html</c>. The browser then reports a broken
    ///         WebAssembly module, which points nowhere near the ordering that caused it.
    ///     </para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="bundlePath">Where the published bundle lives. Defaults to the conventional location.</param>
    /// <param name="pathBase">Prefix to serve the bundle under. Empty serves it at the root.</param>
    public static IEndpointRouteBuilder UseRaskWasmAssets(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null,
        string pathBase = "") =>
        endpoints.UseRask(bundlePath, pathBase, serveIndexFallback: false);

    internal static bool IsFingerprintedAsset(string fileName)
        => _fingerprintRegex.IsMatch(fileName);
}
