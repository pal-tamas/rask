using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     Serves what islands load in the browser, under the app's path base: Rask.External's client script, and the
///     chunks and manifest this project's build bundled.
/// </summary>
/// <remarks>
///     A Web SDK app serves both as static web assets, under its path base. This host is a test assembly, which has no
///     static web assets, and <c>MapRask</c> serves no static files — so without these a page's islands never load.
///     Served from where the build left them, not copied beside the tests: the chunk names are content hashes, so an
///     item list naming them would be stale after every change to the island.
/// </remarks>
internal static class IslandAssets
{
    /// <summary>
    ///     Maps both folders under <paramref name="pathBase" />. Ahead of routing, as an app's static files are: the
    ///     app's catch-all route would otherwise claim the request, and static files never serve a routed one.
    /// </summary>
    public static void Serve(WebApplication app, string pathBase)
    {
        ServeFolder(app, Path.Combine(Repository.Root, "src", "Rask.External", "wwwroot"), pathBase + "/_content/Rask.External");
        ServeFolder(app, Path.Combine(Repository.Root, "tests", "Rask.Server.E2E.Tests", "wwwroot", "_rask", "external"), pathBase + "/_rask/external");
    }

    private static void ServeFolder(IApplicationBuilder app, string directory, string requestPath)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"Nothing to serve at '{directory}'. The islands are bundled by the build of Rask.Server.E2E.Tests, "
                + "which needs node and runs npm the first time.");
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(directory),
            RequestPath = requestPath,
        });
    }
}
