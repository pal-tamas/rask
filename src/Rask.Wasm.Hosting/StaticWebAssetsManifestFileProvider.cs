using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Rask.Wasm.Hosting;

/// <summary>
///     Serves a WASM client's <b>build</b> output by reading its
///     <c>*.staticwebassets.runtime.json</c> manifest, for the dev-time host only.
/// </summary>
/// <remarks>
///     <para>
///         A <see cref="PhysicalFileProvider" /> cannot do this job. The build <c>wwwroot/</c> holds only
///         <c>_framework/</c> — <c>index.html</c>, <c>main.js</c>, <c>rask.wasm.js</c>, <c>global.css</c>
///         and the scoped-asset bundles are not there. They exist as manifest entries pointing into a
///         dozen content roots spread across the client's <c>wwwroot/</c>, its <c>obj/</c>, and the
///         framework's own source tree. Most decisively, <c>/index.html</c> maps to a build-time
///         placeholder-filled shell under <c>obj/…/htmlassetplaceholders/</c>, whose import map and
///         preload hints carry the <i>build</i> fingerprints; the raw <c>index.html</c> in the source
///         tree still has those placeholders empty and cannot boot the runtime.
///     </para>
///     <para>
///         Why hand-rolled rather than ASP.NET's own <c>ManifestStaticWebAssetFileProvider</c>: that type
///         is not public API, and a dev convenience in a shipped package must not depend on framework
///         internals that can move between patch releases. The format read here is small and stable.
///     </para>
///     <para>
///         <b>Development only.</b> The published-bundle path is untouched — and has to stay that way,
///         because a published bundle is trimmed, and trimming folds
///         <c>MetadataUpdater.IsSupported</c> to false so hot reload could never work in it regardless.
///     </para>
/// </remarks>
internal sealed class StaticWebAssetsManifestFileProvider : IFileProvider
{
    private readonly string _manifestPath;
    private readonly Lock _gate = new();

    private Manifest? _manifest;
    private DateTime _loadedStamp;

    public StaticWebAssetsManifestFileProvider(string manifestPath) => _manifestPath = manifestPath;

    public IFileInfo GetFileInfo(string subpath)
    {
        var node = Find(subpath, out var manifest);
        if (node?.Asset is not { } asset || manifest is null)
        {
            return new NotFoundFileInfo(subpath);
        }

        var full = Path.Combine(manifest.ContentRoots[asset.ContentRootIndex], asset.SubPath);
        var file = new FileInfo(full);
        return file.Exists ? new PhysicalFileInfo(file) : new NotFoundFileInfo(subpath);
    }

    /// <summary>
    ///     Needed as well as <see cref="GetFileInfo" />: <c>UseDefaultFiles</c> enumerates a directory to
    ///     find <c>index.html</c> before anything asks for a file, so a provider that only answers
    ///     <see cref="GetFileInfo" /> serves a 404 at <c>/</c>.
    /// </summary>
    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var node = Find(subpath, out var manifest);
        if (node?.Children is not { Count: > 0 } children || manifest is null)
        {
            return NotFoundDirectoryContents.Singleton;
        }

        var entries = new List<IFileInfo>(children.Count);
        foreach (var (name, child) in children)
        {
            if (child.Asset is { } asset)
            {
                var file = new FileInfo(Path.Combine(manifest.ContentRoots[asset.ContentRootIndex], asset.SubPath));
                if (file.Exists)
                {
                    entries.Add(new PhysicalFileInfo(file));
                }
            }
            else if (child.Children is { Count: > 0 })
            {
                // A directory the manifest knows about but that has no single physical home.
                entries.Add(new NotFoundFileInfo(name));
            }
        }

        return new EnumerableDirectoryContents(entries);
    }

    /// <summary>
    ///     No change tokens. Under <c>dotnet watch</c> a rude edit restarts the host, and a hot-applied
    ///     edit does not move any asset — so nothing here would ever fire, and pretending otherwise would
    ///     only cost a file watcher per request path.
    /// </summary>
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private Node? Find(string subpath, out Manifest? manifest)
    {
        manifest = Load();
        if (manifest is null)
        {
            return null;
        }

        var node = manifest.Root;
        foreach (var segment in Split(subpath))
        {
            if (node.Children is not { } children || !children.TryGetValue(segment, out var next))
            {
                return MatchPattern(node, subpath, manifest);
            }

            node = next;
        }

        return node;
    }

    /// <summary>
    ///     Content roots contributed wholesale (<c>_content/{Package}/**</c>) appear as a pattern rather
    ///     than as enumerated children. Only <c>**</c> is ever emitted, so the remaining request path maps
    ///     straight onto the root.
    /// </summary>
    private static Node? MatchPattern(Node node, string subpath, Manifest manifest)
    {
        if (node.Patterns is not { Length: > 0 } patterns)
        {
            return null;
        }

        foreach (var pattern in patterns)
        {
            var root = manifest.ContentRoots[pattern.ContentRootIndex];
            var candidate = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar, Split(subpath))));

            // Never let a crafted path climb out of the content root.
            if (!candidate.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal) || !File.Exists(candidate))
            {
                continue;
            }

            return new Node
            {
                Asset = new Asset { ContentRootIndex = pattern.ContentRootIndex, SubPath = Path.GetRelativePath(root, candidate) }
            };
        }

        return null;
    }

    private static string[] Split(string subpath) =>
        subpath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private Manifest? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_manifestPath))
            {
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(_manifestPath);
            if (_manifest is not null && stamp == _loadedStamp)
            {
                return _manifest;
            }

            try
            {
                using var stream = File.OpenRead(_manifestPath);
                var parsed = JsonSerializer.Deserialize(stream, ManifestJson.Default.Manifest);
                if (parsed?.Root is null)
                {
                    return _manifest;
                }

                Normalize(parsed.Root);
                _manifest = parsed;
                _loadedStamp = stamp;
            }
            catch (JsonException)
            {
                // A half-written manifest during a rebuild — keep serving the previous one.
            }
            catch (IOException)
            {
            }

            return _manifest;
        }
    }

    /// <summary>
    ///     One pass that does two things the deserialized shape can't give us directly.
    ///     <para>
    ///         <b>Drops every precompressed entry.</b> The dev host must serve identity-encoded HTML so
    ///         <c>dotnet watch</c>'s browser-refresh injector can rewrite it — a gzipped shell silently
    ///         loses the script tag that arms the in-browser delta applier. It also removes the only
    ///         entries whose <c>SubPath</c> carries a <c>{0}</c> fingerprint placeholder, so nothing
    ///         downstream has to resolve one.
    ///     </para>
    ///     <para>
    ///         <b>Rebuilds each children map case-insensitively</b>, matching how the static-file
    ///         middleware resolves request paths. Done here rather than with a JsonConverter because the
    ///         source-generated serializer cannot reach a private nested converter type.
    ///     </para>
    /// </summary>
    private static void Normalize(Node node)
    {
        if (node.Children is not { Count: > 0 } children)
        {
            return;
        }

        var rebuilt = new Dictionary<string, Node>(children.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, child) in children)
        {
            if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Normalize(child);
            rebuilt[name] = child;
        }

        node.Children = rebuilt;
    }

    private sealed class EnumerableDirectoryContents(IReadOnlyList<IFileInfo> entries) : IDirectoryContents
    {
        public bool Exists => entries.Count > 0;

        public IEnumerator<IFileInfo> GetEnumerator() => entries.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class Manifest
    {
        public string[] ContentRoots { get; set; } = [];

        public Node Root { get; set; } = new();
    }

    internal sealed class Node
    {
        // Rebuilt with an ordinal-ignore-case comparer by Normalize() after deserialization.
        public Dictionary<string, Node>? Children { get; set; }

        public Asset? Asset { get; set; }

        public Pattern[]? Patterns { get; set; }
    }

    internal sealed class Asset
    {
        public int ContentRootIndex { get; set; }

        public string SubPath { get; set; } = string.Empty;
    }

    internal sealed class Pattern
    {
        public int ContentRootIndex { get; set; }

        public string PatternText { get; set; } = string.Empty;

        public int Depth { get; set; }
    }

}

/// <summary>Source-generated metadata — the package is trim- and AOT-analysed under warnings-as-errors.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StaticWebAssetsManifestFileProvider.Manifest))]
[SuppressMessage("Design", "CA1812", Justification = "Instantiated by the source-generated serializer.")]
internal sealed partial class ManifestJson : JsonSerializerContext;
