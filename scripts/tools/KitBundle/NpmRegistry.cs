using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Semver;

namespace KitBundle;

/// <summary>One release of an npm package, as much of it as installing and following it needs.</summary>
internal sealed record NpmRelease(
    string Name,
    SemVersion Version,
    string Tarball,
    string Integrity,
    bool Deprecated,
    IReadOnlyDictionary<string, string> Dependencies,
    IReadOnlyDictionary<string, string> OptionalDependencies,
    IReadOnlyDictionary<string, string> Peers,
    IReadOnlyList<string> Os,
    IReadOnlyList<string> Cpu);

/// <summary>Every release of a package, and its dist-tags.</summary>
internal sealed record NpmPackument(string Name, IReadOnlyDictionary<string, string> Tags, IReadOnlyList<NpmRelease> Releases);

/// <summary>npm's registry, read the way npm reads it, with no npm.</summary>
internal static class NpmRegistry
{
    internal const string Origin = "https://registry.npmjs.org";

    // The abbreviated document npm itself installs from: every release's dependencies and dist, none of its readme.
    private const string Abbreviated = "application/vnd.npm.install-v1+json";

    private static readonly ConcurrentDictionary<string, Lazy<Task<NpmPackument>>> Packuments = new(StringComparer.Ordinal);

    /// <summary>Every release of <paramref name="name" />; asked once per run, however many packages depend on it.</summary>
    internal static Task<NpmPackument> Packument(string name) =>
        Packuments.GetOrAdd(name, n => new Lazy<Task<NpmPackument>>(() => FetchPackument(n))).Value;

    /// <summary>One release of <paramref name="name" /> — a version or a dist-tag — which must be stable and carry a SHA-512.</summary>
    internal static async Task<NpmRelease> Release(string name, string versionOrTag)
    {
        var url = $"{Origin}/{Encode(name)}/{versionOrTag}";
        using var document = JsonDocument.Parse(await Upstream.Get(url));
        var release = Read(name, document.RootElement)
                      ?? throw new KitBundleException($"npm's {versionOrTag} {name} names no sha512 tarball");
        return release.Version.IsPrerelease
            ? throw new KitBundleException($"npm's {versionOrTag} {name} is {release.Version}, not a stable release")
            : release;
    }

    /// <summary>The tarball at <paramref name="url" />, refused unless it hashes to <paramref name="integrity" />.</summary>
    internal static async Task<byte[]> Tarball(string url, string integrity)
    {
        var bytes = await Upstream.Get(url);
        var actual = Integrity(bytes);
        return string.Equals(actual, integrity, StringComparison.Ordinal)
            ? bytes
            : throw new KitBundleException($"{url} is not the tarball that is locked ({actual}, not {integrity})");
    }

    /// <summary>The Subresource-Integrity form npm publishes: <c>sha512-</c> and base64.</summary>
    internal static string Integrity(byte[] bytes) => "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes));

    private static async Task<NpmPackument> FetchPackument(string name)
    {
        using var document = JsonDocument.Parse(await Upstream.Get($"{Origin}/{Encode(name)}", Abbreviated));
        var root = document.RootElement;
        var tags = Strings(root, "dist-tags");
        var releases = root.TryGetProperty("versions", out var versions)
            ? versions.EnumerateObject().Select(v => Read(name, v.Value)).OfType<NpmRelease>().ToList()
            : [];
        return new NpmPackument(name, tags, releases);
    }

    private static NpmRelease? Read(string name, JsonElement release)
    {
        if (!release.TryGetProperty("version", out var version)
            || !SemVersion.TryParse(version.GetString(), SemVersionStyles.Strict, out var parsed)
            || !release.TryGetProperty("dist", out var dist)
            || !dist.TryGetProperty("tarball", out var tarball)
            || !dist.TryGetProperty("integrity", out var integrity)
            || integrity.GetString() is not { } sri
            || !sri.StartsWith("sha512-", StringComparison.Ordinal))
        {
            return null;
        }

        var optionalPeers = release.TryGetProperty("peerDependenciesMeta", out var meta)
            ? meta.EnumerateObject()
                .Where(p => p.Value.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal)
            : [];

        return new NpmRelease(
            name,
            parsed,
            tarball.GetString()!,
            sri,
            release.TryGetProperty("deprecated", out var deprecated) && deprecated.ValueKind == JsonValueKind.String,
            Strings(release, "dependencies"),
            Strings(release, "optionalDependencies"),
            Strings(release, "peerDependencies").Where(p => !optionalPeers.Contains(p.Key)).ToDictionary(StringComparer.Ordinal),
            List(release, "os"),
            List(release, "cpu"));
    }

    private static Dictionary<string, string> Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static List<string> List(JsonElement element, string property) =>
        element.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]
            : [];

    private static string Encode(string name) => name.Replace("/", "%2f", StringComparison.Ordinal);
}
