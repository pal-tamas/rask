using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Rask.Hosting.Shared;

namespace Rask.Spa.Hosting;

/// <summary>
///     Serves a built single-page app from an ASP.NET host — a TypeScript front end's bundle or a Rask
///     WebAssembly app — with its cache headers and the fallback that keeps client-side routes working on
///     a refresh or a deep link.
/// </summary>
/// <remarks>
///     The framework is not this package's business — React, Vue and Angular all bundle to the same
///     thing, and the cache rules below are keyed on what the <em>bundler</em> guarantees rather than on
///     who generated it. A Rask WebAssembly bundle is recognised from its files and gets the rules its
///     publish guarantees instead. The language is: the contracts Rask generates for a front end are
///     TypeScript, and the build refuses a client that cannot check them (RASKSPA004).
/// </remarks>
public static class RaskSpaEndpointExtensions
{
    private static readonly FileExtensionContentTypeProvider _contentTypes = BuildContentTypes();

    /// <summary>
    ///     The extension-to-MIME table, plus the precompressed suffixes and what a .NET WebAssembly
    ///     publish ships.
    /// </summary>
    /// <remarks>
    ///     <see cref="PrecompressedFileMiddleware" /> rewrites the request to a <c>.br</c>/<c>.gz</c>
    ///     sibling, and the static-file middleware refuses to serve an extension it does not recognise —
    ///     so without those two entries every precompressed asset 404s, which is a worse failure than
    ///     the compression was a win. The placeholder type never reaches the client —
    ///     <c>OnPrepareResponse</c> replaces it with the underlying asset's real type.
    ///     <para>
    ///         The rest are the runtime files a WebAssembly bundle carries besides <c>.wasm</c> and
    ///         <c>.js</c>: ICU data, the boot manifest's binary form, and symbols when a debug build ships
    ///         them. Naming them is the narrow way to serve them; <c>ServeUnknownFileTypes</c> opens the
    ///         directory to every extension there is. A bundler never emits any of them.
    ///     </para>
    /// </remarks>
    private static FileExtensionContentTypeProvider BuildContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".br"] = "application/octet-stream";
        provider.Mappings[".gz"] = "application/octet-stream";
        provider.Mappings[".dat"] = "application/octet-stream";
        provider.Mappings[".blat"] = "application/octet-stream";
        provider.Mappings[".dll"] = "application/octet-stream";
        provider.Mappings[".pdb"] = "application/octet-stream";
        provider.Mappings[".symbols"] = "application/octet-stream";
        return provider;
    }

    /// <summary>
    ///     Serves a built single-page app: a bundler's output, or a Rask WebAssembly app.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Map your API before calling this</b> — as a convention, not a correctness rule. The
    ///         fallback registered here is lowest-precedence, so <c>MapRaskCqrs()</c>, a minimal API or a
    ///         health check still wins for the paths it names whichever side of this call it sits on.
    ///         Keeping them above it is what makes the file readable.
    ///     </para>
    ///     <para>
    ///         A Rask WebAssembly app is served the same way, recognised by its files. Its runtime files
    ///         under <c>/_framework/</c> are cached for ever only when the SDK fingerprinted them, its
    ///         scoped assets under <c>/_rask/a/</c> always are, and a missing file under either is a 404.
    ///         In a <c>rask dev</c> session the client's build output is served instead of its publish,
    ///         which is what lets hot reload reach the browser.
    ///     </para>
    ///     <para>
    ///         There is deliberately no <c>UseRask</c> overload: <c>Rask.Server</c> declares a
    ///         <c>UseRask</c> whose second parameter is a route pattern rather than a path, and an app
    ///         serving a SPA beside the server-rendered operator dashboard references both.
    ///     </para>
    ///     <para>
    ///         With no build output and <c>IHostEnvironment.IsDevelopment()</c>, this answers 200 with a
    ///         page saying where the app is rather than 503 — in development the missing output is the
    ///         normal state, and a 503 sends people hunting a server bug when the answer is that they
    ///         opened the wrong port.
    ///     </para>
    /// </remarks>
    /// <param name="endpoints">The app's endpoint route builder; must also be an <see cref="IApplicationBuilder" />.</param>
    /// <param name="distPath">Where the built app lives. Omit to resolve it the usual way.</param>
    /// <param name="pathBase">Prefix to serve the app under. Empty serves it at the root.</param>
    /// <param name="configure">
    ///     Adjusts <see cref="SpaHostingOptions" />, after the <c>Rask:Spa</c> configuration section, so code
    ///     wins. The two delegates can only be set here.
    /// </param>
    /// <returns><paramref name="endpoints" />, for chaining.</returns>
    public static IEndpointRouteBuilder UseRaskSpa(
        this IEndpointRouteBuilder endpoints,
        string? distPath = null,
        string pathBase = "",
        Action<SpaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Rask:Spa first, then configure. Bound here rather than registered: these options are read while this
        // call maps the app, and each mount keeps its own, so they never live in DI.
        var options = RaskOptionsRegistration.BindNow<SpaHostingOptions>(
            endpoints.ServiceProvider, "Rask:Spa", static (section, o) => section.Bind(o), configure);

        var prefix = SpaPath.Normalize(pathBase);
        var environment = endpoints.ServiceProvider.GetService<IHostEnvironment>();
        var entry = Assembly.GetEntryAssembly();

        // Decided BEFORE the built app is looked for, and deliberately: a development session skips the
        // WebAssembly client's publish, so there usually is no published bundle at all, and looking for
        // one first would 503 exactly the setup the session creates. An explicit distPath is a statement
        // about what to serve and always wins.
        var devManifest = distPath is null ? DevManifest(environment, entry) : null;
        var resolved = devManifest is null
            ? SpaAppBundle.Resolve(distPath, environment?.ContentRootPath, entry, options.IndexFileName)
            : null;

        if (devManifest is null && resolved is null)
        {
            MapMissingBundle(endpoints, prefix, options, environment, entry, distPath);
            return endpoints;
        }

        if (endpoints is not IApplicationBuilder app)
        {
            throw new InvalidOperationException(
                "UseRaskSpa must be called on an IEndpointRouteBuilder that is also an "
                + "IApplicationBuilder (e.g. WebApplication).");
        }

        var dev = devManifest is not null;
        IFileProvider fileProvider = dev
            ? new StaticWebAssetsManifestFileProvider(devManifest!)
            : new PhysicalFileProvider(resolved!);

        var wasm = SpaAppBundle.IsRaskWasm(fileProvider);
        if (wasm)
        {
            ShareScopedAssetsWithWebRoot(endpoints.ServiceProvider, fileProvider, resolved);
        }

        if (dev)
        {
            Console.WriteLine(
                $"Rask.Spa.Hosting: serving the WebAssembly client's build output (hot reload) from {devManifest}");
        }

        // Precompressed siblings first, so a .br/.gz emitted by the build is served as-is with no
        // request-time CPU. Falls straight through when there are none.
        //
        // Neither compression path runs in a development session. `dotnet watch`'s browser-refresh
        // middleware injects its script by rewriting the HTML body and cannot rewrite an encoded one —
        // and that script is the only thing the in-browser delta applier arms on. A compressed shell
        // means no hot reload, silently.
        if (options.ServePrecompressed && !dev)
        {
            app.UseMiddleware<PrecompressedFileMiddleware>(fileProvider);
        }

        // Only when the app called AddRaskSpaHost() (or registered compression itself). Skipped
        // silently otherwise: the host still works, just uncompressed.
        if (!dev && app.ApplicationServices.GetService<IResponseCompressionProvider>() is not null)
        {
            app.UseResponseCompression();
        }

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            RequestPath = prefix,
            DefaultFileNames = [options.IndexFileName],
        });

        // ServeUnknownFileTypes stays off (the framework default). A build emits known types, and
        // serving anything with any extension out of a directory is a wider door than this needs — so
        // the extensions a build does emit are registered on the provider instead.
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = prefix,
            ContentTypeProvider = _contentTypes,
            OnPrepareResponse = context =>
            {
                // The precompressed middleware may have rewritten the path to a .br/.gz sibling, after
                // which the content type was keyed off that suffix — restore it from the real name.
                var name = StaticSpaFiles.UnderlyingFileName(context.File.Name);

                if (_contentTypes.TryGetContentType(name, out var mime))
                {
                    context.Context.Response.ContentType = mime;
                }
                else if (name.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.ContentType = "text/javascript";
                }

                context.Context.Response.Headers.CacheControl =
                    SpaCacheClassification.IsImmutable(
                        Relative(context.Context.Request.Path.Value, prefix), name, options, wasm)
                        ? "public, max-age=31536000, immutable"
                        : "no-cache";

                // Last, so an app can override anything decided above.
                options.OnPrepareResponse?.Invoke(context);
            },
        });

        // In a development session the index document comes from the manifest, not from next to the
        // bundle: the build output's wwwroot/ holds only _framework/, and the index document maps to a
        // placeholder-filled copy under obj/ whose import map carries the build's fingerprints. The one
        // in the source tree still has those placeholders empty and cannot boot the runtime.
        var indexPath = dev
            ? fileProvider.GetFileInfo(options.IndexFileName).PhysicalPath
              ?? throw new InvalidOperationException(
                  $"The WebAssembly client's build manifest has no {options.IndexFileName} ({devManifest}). "
                  + "Rebuild the client project.")
            : Path.Combine(resolved!, options.IndexFileName);

        StaticSpaFiles.MapCatchAll(endpoints, prefix, async context =>
        {
            if (ShouldRefuse(context, options, prefix, wasm))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(indexPath);
        });

        return endpoints;
    }

    /// <summary>
    ///     The WebAssembly client's build-output manifest, when this process should serve it.
    /// </summary>
    /// <remarks>
    ///     Two gates: Development, and a build that baked the manifest and wrote it. The one-project client
    ///     bakes it on every build that is not a publish; a referenced client project, only when the build
    ///     skipped its publish (<c>RaskSpaBuild=false</c>). A deployment runs outside Development and takes
    ///     the ordinary path, and so does a bundler's SPA, which bakes none. Hot reload is deliberately not a
    ///     gate: <c>dotnet run</c> and <c>rask dev --once</c> have none, and without the manifest they would
    ///     serve a stale publish or nothing at all.
    /// </remarks>
    private static string? DevManifest(IHostEnvironment? environment, Assembly? entry)
    {
        if (environment?.IsDevelopment() != true)
        {
            return null;
        }

        var manifest = SpaAppBundle.Read(entry, SpaAppBundle.DevManifestMetadataKey);
        return manifest is not null && File.Exists(manifest) ? manifest : null;
    }

    /// <summary>
    ///     Makes a WebAssembly bundle's <c>_rask/a/</c> files part of the host's web root.
    /// </summary>
    /// <remarks>
    ///     A host that also runs <c>Rask.Server</c> — the operator dashboard beside the app — has that
    ///     package's <c>/_rask/a/{hash}</c> endpoint, and routing hands a matched endpoint the request
    ///     before the static-file middleware ever sees it. That handler answers a hash its own registry
    ///     lacks from the web root, so the bundle's scoped assets have to be there. A published app whose
    ///     bundle already is the web root needs nothing added; the same mechanism ASP.NET uses for its own
    ///     static web assets in development does the rest.
    /// </remarks>
    private static void ShareScopedAssetsWithWebRoot(
        IServiceProvider services,
        IFileProvider bundle,
        string? bundleDirectory)
    {
        if (services.GetService<IWebHostEnvironment>() is not { } web)
        {
            return;
        }

        if (bundleDirectory is not null
            && !string.IsNullOrEmpty(web.WebRootPath)
            && string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundleDirectory)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(web.WebRootPath)),
                StringComparison.Ordinal))
        {
            return;
        }

        web.WebRootFileProvider = new CompositeFileProvider(
            web.WebRootFileProvider,
            new SubtreeFileProvider(bundle, "_rask/a/"));
    }

    /// <summary>The request path with the host's prefix removed, so options read as the app wrote them.</summary>
    private static string? Relative(string? path, string prefix) =>
        prefix.Length > 0 && path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..]
            : path;

    private static bool ShouldRefuse(HttpContext context, SpaHostingOptions options, string prefix, bool wasm) =>
        options.ExcludeFromFallback is { } custom
            ? custom(context)
            : DefaultShouldRefuse(context, options, prefix, wasm);

    /// <summary>
    ///     Whether a request that matched no file and no endpoint should be a 404 rather than the index
    ///     document.
    /// </summary>
    /// <remarks>
    ///     Two rules. A request under a path that only ever holds files is an asset by construction, and
    ///     a missing one is a missing file. And a request whose <c>Accept</c> header asks for something
    ///     other than HTML is not a navigation — a module import answered with HTML fails as
    ///     <c>Failed to load module script</c>, and a runtime file answered with HTML as a broken
    ///     WebAssembly module, both of which read as a broken framework rather than a 404. A request
    ///     stating no preference is treated as a navigation, because that is what a bare <c>curl</c> or
    ///     an old client looks like.
    /// </remarks>
    internal static bool DefaultShouldRefuse(
        HttpContext context,
        SpaHostingOptions options,
        string prefix,
        bool wasm = false)
    {
        if (SpaCacheClassification.IsAssetPath(Relative(context.Request.Path.Value, prefix), options, wasm))
        {
            return true;
        }

        var accept = context.Request.Headers.Accept.ToString();
        if (accept.Length == 0)
        {
            return false;
        }

        return !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
               && !accept.Contains("*/*", StringComparison.Ordinal);
    }

    private static void MapMissingBundle(
        IEndpointRouteBuilder endpoints,
        string prefix,
        SpaHostingOptions options,
        IHostEnvironment? environment,
        Assembly? entry,
        string? distPath)
    {
        var wasmClient = SpaAppBundle.Read(entry, SpaAppBundle.WasmClientMetadataKey);
        var client = SpaAppBundle.Read(entry, SpaAppBundle.ClientMetadataKey);
        var devServer = options.DevServerUrl ?? SpaAppBundle.Read(entry, SpaAppBundle.DevServerMetadataKey);
        var buildHint = wasmClient is not null
            ? $"publish {wasmClient}"
            : client is null
                ? "run your bundler's build"
                : $"run the build in {client}";

        if (environment?.IsDevelopment() == true)
        {
            Console.WriteLine(wasmClient is not null
                ? "Rask.Spa.Hosting: the WebAssembly client has no build output to serve. Building this "
                  + $"project builds the client too; to serve the published app from this host instead, {buildHint}."
                : "Rask.Spa.Hosting: no built app to serve. In development the front end is served by the "
                  + $"bundler — open {devServer ?? "the bundler's dev server"} (rask dev starts it). To serve "
                  + $"the built app from this host instead, {buildHint}.");

            StaticSpaFiles.MapCatchAll(endpoints, prefix, async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(DevelopmentPage(devServer, buildHint, wasmClient is not null));
            });

            return;
        }

        // Outside development a missing bundle is a real deployment fault, so it fails loudly.
        var reason = distPath is null
            ? "no built app was found (the publish step copies the build output next to the app; "
              + "check that the build ran, or pass distPath explicitly)"
            : $"no built app at {distPath}";

        Console.Error.WriteLine($"Rask.Spa.Hosting: {reason}.");

        StaticSpaFiles.MapCatchAll(endpoints, prefix, async context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync($"The single-page app is unavailable: {reason}.");
        });
    }

    private static string DevelopmentPage(string? devServer, string buildHint, bool wasm)
    {
        var where = wasm
            ? """
              <p>This host serves your WebAssembly app's <em>build output</em>, and there isn't any.
              Build this project — <code>dotnet build</code> or <code>rask dev</code> — and it is served from here.</p>
              """
            : $"""
               <p>This host serves your app's <em>build output</em>, and there isn't any. In development the
               front end is served by the bundler instead, which is the one with hot reload.</p>
               {(devServer is null
                   ? "<p>Start it with <code>rask dev</code>.</p>"
                   : $"""<p><a href="{WebUtility.HtmlEncode(devServer)}">{WebUtility.HtmlEncode(devServer)}</a> &mdash; started by <code>rask dev</code>.</p>""")}
               """;

        return $"""
            <!doctype html>
            <meta charset="utf-8">
            <title>No built app</title>
            <body style="font:16px/1.5 system-ui;max-width:38rem;margin:4rem auto;padding:0 1rem">
            <h1>Nothing built yet</h1>
            {where}
            <p>To serve the built app from this host, {WebUtility.HtmlEncode(buildHint)} and reload.</p>
            <p>The API on this host is unaffected and still answering.</p>
            </body>
            """;
    }
}
