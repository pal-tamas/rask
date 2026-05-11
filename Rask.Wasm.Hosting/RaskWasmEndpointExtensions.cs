using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;

namespace Rask.Wasm.Hosting;

public static class RaskWasmEndpointExtensions
{
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
    public static IEndpointRouteBuilder UseRask(
        this IEndpointRouteBuilder endpoints,
        string? bundlePath = null)
    {
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

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            DefaultFileNames = new[] { "index.html" }
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
            OnPrepareResponse = ctx =>
            {
                var ext = Path.GetExtension(ctx.File.Name);
                if (ext.Equals(".wasm", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.ContentType = "application/wasm";
                }
                else if (ext.Equals(".js", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.ContentType = "application/javascript";
                }

                // Force revalidation on every request. ETag + Last-Modified still give the
                // 304 fast path when content is unchanged, but without `no-cache` browsers
                // (especially for ES module imports) reuse disk-cached copies across page
                // loads without checking — which masks `dotnet build` rebuilds of rask.wasm.js
                // and the published _framework/*.wasm payload.
                ctx.Context.Response.Headers.CacheControl = "no-cache";
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
}
