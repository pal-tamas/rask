using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;
using Rask.TestSupport;

namespace Rask.External.Tests;

/// <summary>
///     An app deployed under a sub-path (<c>LiveOptions.PathBase = "/shop"</c>) still finds its islands: the
///     runtime script, the manifest and every chunk are asked for under the base.
/// </summary>
/// <remarks>
///     The C# half writes the script's URL; the client half reads the base back off that URL, because the
///     chunk URLs in the manifest were baked at build. The client is driven as the JavaScript it ships, loaded
///     in node from the root and from under <c>/shop</c>.
/// </remarks>
[Collection(nameof(PathBaseCollection))]
public partial class ExternalPathBaseTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_runtime_script_is_served_under_the_path_base()
    {
        var page = TitledReport.Id(41);

        var html = RenderUnder("/shop", page);

        Assert.Contains($"src=\"/shop{ExternalDefaults.RuntimeScriptUrl}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_script_is_still_emitted_once_under_a_path_base()
    {
#pragma warning disable RASK014 // the test hands the very instance it renders to the renderer
        var page = new ThreeReports();
#pragma warning restore RASK014

        var html = RenderUnder("/shop", page);

        Assert.Equal(3, html.Split("<rask-external ").Length - 1);
        Assert.Equal(1, html.Split(ExternalDefaults.RuntimeScriptUrl).Length - 1);
    }

    [Fact]
    public void The_default_manifest_is_fetched_under_the_path_base()
    {
        // Node-less machines skip: the fixture is the shipped runtime, run in node.
        if (Deploys() is not { } deploys)
        {
            return;
        }

        var fetched = Strings(deploys.GetProperty("shop"), "fetched");

        Assert.Contains("/shop/_rask/external/manifest.json", fetched);
        Assert.Contains("/shop/_content/Acme.Ui/manifest.json", fetched);
        Assert.Empty(Strings(deploys.GetProperty("shop"), "errors"));
    }

    [Fact]
    public void A_root_relative_chunk_is_imported_under_the_path_base()
    {
        if (Deploys() is not { } deploys)
        {
            return;
        }

        var shop = deploys.GetProperty("shop");

        Assert.Contains("/shop/_rask/external/assets/Chart.js", Strings(shop, "imported"));
        Assert.Contains("Chart", Strings(shop, "mounted"));
        Assert.Equal("http://localhost:5174/@fs/app/Live.entry.ts", shop.GetProperty("devChunk").GetString());
    }

    [Fact]
    public void The_path_base_is_empty_when_the_runtime_is_served_from_the_root()
    {
        if (Deploys() is not { } deploys)
        {
            return;
        }

        var root = deploys.GetProperty("root");

        Assert.Equal(string.Empty, root.GetProperty("base").GetString());
        Assert.Equal("/shop", deploys.GetProperty("shop").GetProperty("base").GetString());
        Assert.Contains("/_rask/external/manifest.json", Strings(root, "fetched"));
        Assert.Contains("/_rask/external/assets/Chart.js", Strings(root, "imported"));
    }

    [Fact]
    public void A_url_already_under_the_path_base_is_not_prefixed_twice()
    {
        if (Deploys() is not { } deploys)
        {
            return;
        }

        var imported = Strings(deploys.GetProperty("shop"), "imported");

        Assert.Contains("/shop/_rask/external/assets/Already.js", imported);
        Assert.DoesNotContain(imported, path => path!.StartsWith("/shop/shop/", StringComparison.Ordinal));
    }

    private static JsonElement? Deploys() => NodeFixture.Run(
        "ExternalPathBaseFixture",
        Path.Combine(IslandBuild.RepoRoot(), "src", "Rask.External", "wwwroot", "rask-external.js"));

    private static string?[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(e => e.GetString())];

    private static string RenderUnder(string pathBase, Component page)
    {
        var previous = LiveOptions.PathBase;
        LiveOptions.PathBase = pathBase;
        try
        {
#pragma warning disable RASK014 // the document shell every host installs, built by hand around the page under test
            return new RootErrorBoundary(page).RenderAsLiveRoot(RenderHarness.EmptyServices());
#pragma warning restore RASK014
        }
        finally
        {
            LiveOptions.PathBase = previous;
        }
    }
}

/// <summary><see cref="LiveOptions.PathBase" /> is process-wide, so the tests that set it never run beside others.</summary>
[CollectionDefinition(nameof(PathBaseCollection), DisableParallelization = true)]
public sealed class PathBaseCollection;
