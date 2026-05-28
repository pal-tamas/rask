using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Rask.Wasm.Hosting;

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
    ///     Generic form of <see cref="UseRask(IEndpointRouteBuilder, string?)" /> that
    ///     additionally touches <typeparamref name="TApp" /> so the runtime loads the
    ///     consumer's component assembly. The assembly's source-generator-emitted
    ///     <c>__RaskScopedCssRegistration</c> / <c>__RaskScopedJsRegistration</c> classes
    ///     carry <c>[ModuleInitializer]</c>, which only fires on assembly load. Without
    ///     that touch the host process never realises the assembly exists (only
    ///     <c>Rask.Wasm.Hosting</c> is referenced from <see cref="Program" /> in a typical
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
    public static IEndpointRouteBuilder UseRask<TApp>(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null)
    {
        // typeof(TApp) is the load trigger — references the type token, which forces the
        // runtime to resolve and JIT-init the defining assembly, which runs every
        // [ModuleInitializer] in that assembly. Discarded to make the intent obvious.
        _ = typeof(TApp);
        return UseRask(endpoints, bundlePath);
    }

    public static IEndpointRouteBuilder UseRask(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null)
    {
        // Per-component scoped asset endpoint. Registered before the static-file middleware
        // so a /_rask/a/{hash}.{ext} URL is served from the in-process ScopedAssetRegistry
        // even if the published bundle happens to contain a same-named file. The shared
        // static registry is populated by module initializers from the referenced WASM
        // assembly (the AppBundle's referenced project) — present as soon as that assembly
        // is loaded into this host process.
        RaskAssetEndpoint.MapRaskAssets(endpoints);

        var resolved = bundlePath ?? WasmAppBundle.ResolveFromAssembly(Assembly.GetEntryAssembly());

        if (string.IsNullOrEmpty(resolved) || !Directory.Exists(resolved))
        {
            var reason = string.IsNullOrEmpty(resolved)
                ? "no bundle configured (add a <ProjectReference> to a project that sets <WasmGenerateAppBundle>true</WasmGenerateAppBundle>, with ReferenceOutputAssembly=\"false\" SkipGetTargetFrameworkProperties=\"true\", or pass bundlePath explicitly)"
                : $"bundle not found at {resolved} (run `dotnet publish` on the WASM project)";
            Console.Error.WriteLine($"Rask.Wasm.Hosting: {reason}.");

            endpoints.MapFallback(async ctx =>
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

        var fileProvider = new PhysicalFileProvider(resolved);

        // Precompressed siblings first: if the WASM publish step emitted .br/.gz next to
        // each asset (set <CompressionEnabled>true</CompressionEnabled> on the WASM
        // project), serve those directly with the matching Content-Encoding header — zero
        // request-time CPU and CDN-cacheable. Falls through to UseStaticFiles + the
        // runtime UseResponseCompression below when no sibling exists.
        app.UseMiddleware<PrecompressedFileMiddleware>(fileProvider);

        // If the host called services.AddRask() (which registers brotli/gzip providers and
        // adds application/wasm + application/octet-stream to the compressible MIME set),
        // wire UseResponseCompression ahead of UseStaticFiles so .wasm/.dll/.js/.json bodies
        // ship compressed. Skipped silently when not registered — the host still works,
        // just without compression. With the precompressed-sibling middleware ahead, this
        // only fires for files without a baked sibling (e.g. index.html if not pre-encoded).
        if (app.ApplicationServices.GetService<IResponseCompressionProvider>() is not null)
        {
            app.UseResponseCompression();
        }

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider, DefaultFileNames = new[] { "index.html" }
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
            OnPrepareResponse = ctx =>
            {
                // The precompressed middleware may have rewritten the path to a .br/.gz
                // sibling — strip the encoding suffix so the MIME/cache classification
                // reflects the underlying asset (foo.wasm.br is still "application/wasm").
                var name = ctx.File.Name;
                if (name.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^3];
                }

                var ext = Path.GetExtension(name);
                if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.ContentType = "application/wasm";
                }
                else if (ext.Equals(".js", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.ContentType = "application/javascript";
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

        var indexPath = Path.Combine(resolved, "index.html");
        endpoints.MapFallback(async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            await ctx.Response.SendFileAsync(indexPath);
        });

        return endpoints;
    }

    internal static bool IsFingerprintedAsset(string fileName)
        => _fingerprintRegex.IsMatch(fileName);
}
