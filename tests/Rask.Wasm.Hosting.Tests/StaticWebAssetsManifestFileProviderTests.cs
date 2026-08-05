using System.Text.Json;
using Rask.Wasm.Hosting;

namespace Rask.Wasm.Hosting.Tests;

/// <summary>
///     The dev-bundle file provider. It exists because a WASM client's <b>build</b> output cannot be
///     served from disk directly: <c>bin/…/wwwroot/</c> holds only <c>_framework/</c>, and everything
///     else — the shell, <c>main.js</c>, <c>rask.wasm.js</c>, scoped-asset bundles, RCL content — lives
///     in other content roots that only the static-web-assets manifest knows about.
/// </summary>
public sealed class StaticWebAssetsManifestFileProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-swa-" + Guid.NewGuid().ToString("N"));

    public StaticWebAssetsManifestFileProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void An_asset_resolves_through_its_content_root()
    {
        // The shape that matters: the file lives nowhere near the bundle directory, which is exactly why
        // a PhysicalFileProvider over bin/…/wwwroot cannot serve it.
        var elsewhere = Seed("framework-source", "rask.wasm.js", "export function boot() {}");
        var provider = Provider(Manifest(
            [elsewhere],
            ("rask.wasm.js", 0, "rask.wasm.js")));

        var file = provider.GetFileInfo("rask.wasm.js");

        Assert.True(file.Exists);
        Assert.Equal(Path.Combine(elsewhere, "rask.wasm.js"), file.PhysicalPath);
    }

    [Fact]
    public void A_nested_path_walks_the_children()
    {
        var root = Seed("obj-content", Path.Combine("css", "app.css"), ".a{}");
        var provider = Provider(Manifest([root], ("_content/Pkg/css/app.css", 0, "css/app.css")));

        Assert.True(provider.GetFileInfo("_content/Pkg/css/app.css").Exists);
        Assert.True(provider.GetFileInfo("/_content/Pkg/css/app.css").Exists);
    }

    [Fact]
    public void Lookup_is_case_insensitive_like_the_static_file_middleware()
    {
        var root = Seed("content", "Index.html", "<html></html>");
        var provider = Provider(Manifest([root], ("index.html", 0, "Index.html")));

        Assert.True(provider.GetFileInfo("INDEX.HTML").Exists);
    }

    [Fact]
    public void Precompressed_entries_are_dropped()
    {
        // Load-bearing, not tidiness: `dotnet watch`'s browser-refresh middleware injects its script by
        // rewriting the HTML body and cannot rewrite an encoded one — and that script is the only thing
        // the in-browser delta applier arms on. Serving a .gz shell means no hot reload, silently.
        // Dropping them also removes the only SubPaths carrying a {0} fingerprint placeholder.
        var root = Seed("content", "index.html", "<html></html>");
        Seed("content", "index.html.gz", "compressed");
        var provider = Provider(Manifest(
            [root],
            ("index.html", 0, "index.html"),
            ("index.html.gz", 0, "index.html.gz")));

        Assert.True(provider.GetFileInfo("index.html").Exists);
        Assert.False(provider.GetFileInfo("index.html.gz").Exists);
    }

    [Fact]
    public void A_missing_entry_and_a_missing_file_both_report_not_found()
    {
        var root = Seed("content", "present.txt", "hi");
        var provider = Provider(Manifest(
            [root],
            ("present.txt", 0, "present.txt"),
            ("ghost.txt", 0, "not-on-disk.txt")));

        Assert.False(provider.GetFileInfo("nothing-here.txt").Exists);
        Assert.False(provider.GetFileInfo("ghost.txt").Exists);
    }

    [Fact]
    public void The_root_directory_lists_its_files_so_default_files_can_find_the_shell()
    {
        // UseDefaultFiles enumerates the directory before anything asks for a file; a provider that only
        // answers GetFileInfo serves a 404 at "/".
        var root = Seed("content", "index.html", "<html></html>");
        var provider = Provider(Manifest([root], ("index.html", 0, "index.html")));

        var contents = provider.GetDirectoryContents(string.Empty);

        Assert.True(contents.Exists);
        Assert.Contains(contents, f => f.Name == "index.html");
    }

    [Fact]
    public void A_rebuilt_manifest_is_picked_up()
    {
        var root = Seed("content", "one.txt", "1");
        Seed("content", "two.txt", "2");
        var path = Write(Manifest([root], ("one.txt", 0, "one.txt")));
        var provider = new StaticWebAssetsManifestFileProvider(path);

        Assert.False(provider.GetFileInfo("two.txt").Exists);

        // A rebuild rewrites the manifest; the host process outlives it.
        File.WriteAllText(path, Manifest([root], ("one.txt", 0, "one.txt"), ("two.txt", 0, "two.txt")));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

        Assert.True(provider.GetFileInfo("two.txt").Exists);
    }

    [Fact]
    public void A_missing_manifest_is_not_fatal()
    {
        var provider = new StaticWebAssetsManifestFileProvider(Path.Combine(_root, "absent.json"));

        Assert.False(provider.GetFileInfo("index.html").Exists);
        Assert.False(provider.GetDirectoryContents(string.Empty).Exists);
    }

    [Fact]
    public void A_half_written_manifest_keeps_serving_the_previous_one()
    {
        // A rebuild truncates and rewrites the file; a request landing mid-write must not take the host
        // down or start 404ing every asset.
        var root = Seed("content", "one.txt", "1");
        var path = Write(Manifest([root], ("one.txt", 0, "one.txt")));
        var provider = new StaticWebAssetsManifestFileProvider(path);
        Assert.True(provider.GetFileInfo("one.txt").Exists);

        File.WriteAllText(path, "{ \"ContentRoots\": [");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

        Assert.True(provider.GetFileInfo("one.txt").Exists);
    }

    private StaticWebAssetsManifestFileProvider Provider(string manifest) =>
        new(Write(manifest));

    private string Write(string manifest)
    {
        var path = Path.Combine(_root, "app.staticwebassets.runtime.json");
        File.WriteAllText(path, manifest);
        return path;
    }

    private string Seed(string rootName, string relative, string content)
    {
        var root = Path.Combine(_root, rootName);
        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return root;
    }

    /// <summary>Builds the real manifest shape: content roots plus a path trie of assets.</summary>
    private static string Manifest(string[] contentRoots, params (string Url, int Root, string SubPath)[] assets)
    {
        var root = new Dictionary<string, object?>();

        foreach (var (url, rootIndex, subPath) in assets)
        {
            var node = root;
            var segments = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++)
            {
                if (i == segments.Length - 1)
                {
                    node[segments[i]] = new Dictionary<string, object?>
                    {
                        ["Children"] = null,
                        ["Asset"] = new Dictionary<string, object?> { ["ContentRootIndex"] = rootIndex, ["SubPath"] = subPath },
                        ["Patterns"] = null
                    };
                    break;
                }

                if (!node.TryGetValue(segments[i], out var existing) || existing is not Dictionary<string, object?> child)
                {
                    child = new Dictionary<string, object?> { ["Children"] = new Dictionary<string, object?>(), ["Asset"] = null, ["Patterns"] = null };
                    node[segments[i]] = child;
                }

                node = (Dictionary<string, object?>)child["Children"]!;
            }
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["ContentRoots"] = contentRoots,
            ["Root"] = new Dictionary<string, object?> { ["Children"] = root, ["Asset"] = null, ["Patterns"] = null }
        });
    }
}
