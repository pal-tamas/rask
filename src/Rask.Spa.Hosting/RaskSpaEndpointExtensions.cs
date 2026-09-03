using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Rask.Hosting.Shared;

namespace Rask.Spa.Hosting;

/// <summary>
///     Serves a built TypeScript single-page app from an ASP.NET host — the bundle, its cache headers,
///     and the fallback that keeps client-side routes working on a refresh or a deep link.
/// </summary>
/// <remarks>
///     The framework is not this package's business — React, Vue and Angular all bundle to the same
///     thing, and the cache rules below are keyed on what the <em>bundler</em> guarantees rather than on
///     who generated it. The language is: the contracts Rask generates for the client are TypeScript,
///     and the build refuses a client that cannot check them (RASKSPA004).
/// </remarks>
public static class RaskSpaEndpointExtensions
{
    private static readonly FileExtensionContentTypeProvider _contentTypes = BuildContentTypes();

    /// <summary>
    ///     The extension-to-MIME table, plus the two precompressed suffixes.
    /// </summary>
    /// <remarks>
    ///     <see cref="PrecompressedFileMiddleware" /> rewrites the request to a <c>.br</c>/<c>.gz</c>
    ///     sibling, and the static-file middleware refuses to serve an extension it does not recognise —
    ///     so without these two entries every precompressed asset 404s, which is a worse failure than
    ///     the compression was a win. Registering exactly two extensions is the narrow way to fix that:
    ///     the alternative, <c>ServeUnknownFileTypes</c>, opens the directory to every extension there
    ///     is. The placeholder type here never reaches the client — <c>OnPrepareResponse</c> replaces it
    ///     with the underlying asset's real type.
    /// </remarks>
    private static FileExtensionContentTypeProvider BuildContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".br"] = "application/octet-stream";
        provider.Mappings[".gz"] = "application/octet-stream";
        return provider;
    }

    /// <summary>
    ///     Serves the bundler's build output as a single-page app.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Map your API before calling this</b> — as a convention, not a correctness rule. The
    ///         fallback registered here is lowest-precedence, so <c>MapRaskCqrs()</c>, a minimal API or a
    ///         health check still wins for the paths it names whichever side of this call it sits on.
    ///         Keeping them above it is what makes the file readable.
    ///     </para>
    ///     <para>
    ///         There is deliberately no <c>UseRask</c> overload. <c>Rask.Wasm.Hosting</c> already
    ///         declares <c>UseRask(IEndpointRouteBuilder, string?, string)</c> with this exact shape and
    ///         <c>Rask.Server</c> declares one whose second parameter is a route pattern rather than a
    ///         path. A third would be a genuine ambiguity in an app holding two of those usings, and the
    ///         near-miss is bad enough already.
    ///     </para>
    ///     <para>
    ///         With no build output and <c>IHostEnvironment.IsDevelopment()</c>, this answers 200 with a
    ///         page pointing at the bundler's dev server rather than 503 — in development the missing
    ///         <c>dist/</c> is the normal state, and a 503 sends people hunting a server bug when the
    ///         answer is that they opened the wrong port.
    ///     </para>
    /// </remarks>
    /// <param name="endpoints">The app's endpoint route builder; must also be an <see cref="IApplicationBuilder" />.</param>
    /// <param name="distPath">Where the built app lives. Omit to resolve it the usual way.</param>
    /// <param name="pathBase">Prefix to serve the app under. Empty serves it at the root.</param>
    /// <param name="configure">Adjusts <see cref="SpaHostingOptions" />.</param>
    /// <returns><paramref name="endpoints" />, for chaining.</returns>
    public static IEndpointRouteBuilder UseRaskSpa(
        this IEndpointRouteBuilder endpoints,
        string? distPath = null,
        string pathBase = "",
        Action<SpaHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new SpaHostingOptions();
        configure?.Invoke(options);

        var prefix = SpaPath.Normalize(pathBase);
        var environment = endpoints.ServiceProvider.GetService<IHostEnvironment>();
        var entry = Assembly.GetEntryAssembly();

        var resolved = SpaAppBundle.Resolve(
            distPath, environment?.ContentRootPath, entry, options.IndexFileName);

        if (resolved is null)
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

        var fileProvider = new PhysicalFileProvider(resolved);

        // Precompressed siblings first, so a .br/.gz emitted by the bundler is served as-is with no
        // request-time CPU. Falls straight through when the bundler emitted none, which is the default
        // for every one of these toolchains.
        if (options.ServePrecompressed)
        {
            app.UseMiddleware<PrecompressedFileMiddleware>(fileProvider);
        }

        // Only when the app called AddRaskSpaHost() (or registered compression itself). Skipped
        // silently otherwise: the host still works, just uncompressed.
        if (app.ApplicationServices.GetService<IResponseCompressionProvider>() is not null)
        {
            app.UseResponseCompression();
        }

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            RequestPath = prefix,
            DefaultFileNames = [options.IndexFileName],
        });

        // ServeUnknownFileTypes stays off (the framework default). A bundler emits known types, and
        // serving anything with any extension out of a directory is a wider door than this needs — so
        // the two precompressed suffixes are registered on the provider instead.
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
                        Relative(context.Context.Request.Path.Value, prefix), name, options)
                        ? "public, max-age=31536000, immutable"
                        : "no-cache";

                // Last, so an app can override anything decided above.
                options.OnPrepareResponse?.Invoke(context);
            },
        });

        var indexPath = Path.Combine(resolved, options.IndexFileName);

        StaticSpaFiles.MapCatchAll(endpoints, prefix, async context =>
        {
            if (ShouldRefuse(context, options, prefix))
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

    /// <summary>The request path with the host's prefix removed, so options read as the app wrote them.</summary>
    private static string? Relative(string? path, string prefix) =>
        prefix.Length > 0 && path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..]
            : path;

    private static bool ShouldRefuse(HttpContext context, SpaHostingOptions options, string prefix) =>
        options.ExcludeFromFallback is { } custom
            ? custom(context)
            : DefaultShouldRefuse(context, options, prefix);

    /// <summary>
    ///     Whether a request that matched no file and no endpoint should be a 404 rather than the index
    ///     document.
    /// </summary>
    /// <remarks>
    ///     Two rules. A request under a content-hashed prefix is an asset by construction, and a missing
    ///     one is a missing file. And a request whose <c>Accept</c> header asks for something other than
    ///     HTML is not a navigation — a module import answered with HTML fails as
    ///     <c>Failed to load module script</c>, which reads as a broken framework rather than a 404.
    ///     A request stating no preference is treated as a navigation, because that is what a bare
    ///     <c>curl</c> or an old client looks like.
    /// </remarks>
    internal static bool DefaultShouldRefuse(HttpContext context, SpaHostingOptions options, string prefix)
    {
        var path = Relative(context.Request.Path.Value, prefix);
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var immutablePrefix in options.ImmutablePathPrefixes)
            {
                if (path.StartsWith(immutablePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
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
        var client = SpaAppBundle.Read(entry, SpaAppBundle.ClientMetadataKey);
        var devServer = options.DevServerUrl ?? SpaAppBundle.Read(entry, SpaAppBundle.DevServerMetadataKey);
        var buildHint = client is null
            ? "run your bundler's build"
            : $"run the build in {client}";

        if (environment?.IsDevelopment() == true)
        {
            var target = devServer ?? "the bundler's dev server";
            Console.WriteLine(
                $"Rask.Spa.Hosting: no built app to serve. In development the front end is served by "
                + $"the bundler — open {target} (rask dev starts it). To serve the built app from this "
                + $"host instead, {buildHint}.");

            StaticSpaFiles.MapCatchAll(endpoints, prefix, async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(DevelopmentPage(devServer, buildHint));
            });

            return;
        }

        // Outside development a missing bundle is a real deployment fault, so it fails loudly.
        var reason = distPath is null
            ? "no built app was found (the publish step copies the bundler's output next to the app; "
              + "check that the build ran, or pass distPath explicitly)"
            : $"no built app at {distPath}";

        Console.Error.WriteLine($"Rask.Spa.Hosting: {reason}.");

        StaticSpaFiles.MapCatchAll(endpoints, prefix, async context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync($"The single-page app is unavailable: {reason}.");
        });
    }

    private static string DevelopmentPage(string? devServer, string buildHint)
    {
        var link = devServer is null
            ? "<p>Start it with <code>rask dev</code>.</p>"
            : $"""<p><a href="{WebUtility.HtmlEncode(devServer)}">{WebUtility.HtmlEncode(devServer)}</a> &mdash; started by <code>rask dev</code>.</p>""";

        return $"""
            <!doctype html>
            <meta charset="utf-8">
            <title>No built app</title>
            <body style="font:16px/1.5 system-ui;max-width:38rem;margin:4rem auto;padding:0 1rem">
            <h1>Nothing built yet</h1>
            <p>This host serves your app's <em>build output</em>, and there isn't any. In development the
            front end is served by the bundler instead, which is the one with hot reload.</p>
            {link}
            <p>To serve the built app from this host, {WebUtility.HtmlEncode(buildHint)} and reload.</p>
            <p>The API on this host is unaffected and still answering.</p>
            </body>
            """;
    }
}
