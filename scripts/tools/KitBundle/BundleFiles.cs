using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KitBundle;

/// <summary>
///     An engine's <c>bundle.json</c>: what a person pins. <c>dependencies</c> are bundled (their trees resolved
///     and installed), <c>overrides</c> hold one package at one release wherever the tree asks for it — a hold a
///     person made, which no follower moves — and <c>data</c> are packages read, never bundled or installed.
/// </summary>
internal sealed class BundleManifest
{
    private readonly JsonObject _json;

    private BundleManifest(string path, JsonObject json)
    {
        FilePath = path;
        _json = json;
    }

    internal string FilePath { get; }

    internal IReadOnlyDictionary<string, string> Dependencies => Section("dependencies");

    internal IReadOnlyDictionary<string, string> Overrides => Section("overrides");

    internal IReadOnlyDictionary<string, string> Data => Section("data");

    /// <summary>Every pin a follower moves: what is bundled, then what is read.</summary>
    internal IEnumerable<string> Followed => Dependencies.Keys.Concat(Data.Keys);

    internal static BundleManifest Read(string path) =>
        new(path, JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new KitBundleException($"{path} is not a JSON object"));

    internal string? Pin(string name) =>
        Dependencies.TryGetValue(name, out var pin) || Data.TryGetValue(name, out pin) ? pin : null;

    internal void Move(string name, string version)
    {
        foreach (var section in new[] { "dependencies", "data" })
        {
            if (_json[section] is JsonObject map && map.ContainsKey(name))
            {
                map[name] = version;
            }
        }
    }

    internal void Write() => File.WriteAllText(FilePath, Json.Write(_json));

    private Dictionary<string, string> Section(string name) =>
        _json[name] is JsonObject map
            ? map.ToDictionary(p => p.Key, p => p.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>One locked package: where it is installed (or read), and the tarball that holds it.</summary>
internal sealed record LockedPackage(string Path, string Name, string Version, string Resolved, string Integrity, bool Optional, IReadOnlyList<string> Os, IReadOnlyList<string> Cpu)
{
    /// <summary>Whether this machine installs it: an optional package names the systems it runs on.</summary>
    internal bool InstallsHere => !Optional || (Matches(Os, Platform.NpmOs) && Matches(Cpu, Platform.NpmCpu));

    private static bool Matches(IReadOnlyList<string> allowed, string here) =>
        allowed.Count == 0
        || (!allowed.Contains("!" + here, StringComparer.Ordinal)
            && (allowed.All(a => a.StartsWith('!')) || allowed.Contains(here, StringComparer.Ordinal)));
}

/// <summary>
///     An engine's <c>bundle-lock.json</c>: every package the bundle is built from, at the node_modules path it is
///     installed at, with its tarball and that tarball's SHA-512. Written in path order, so the same tree is the same
///     bytes on every machine; a build installs exactly this and asks the registry for nothing else.
/// </summary>
internal static class BundleLock
{
    internal const string DataPrefix = "data/";

    internal static IReadOnlyList<LockedPackage> Read(string path)
    {
        var json = JsonNode.Parse(File.ReadAllText(path))?["packages"] as JsonObject
                   ?? throw new KitBundleException($"{path} holds no packages");
        return
        [
            .. json.Select(p =>
            {
                var entry = p.Value!.AsObject();
                return new LockedPackage(
                    p.Key,
                    Name(p.Key),
                    entry["version"]!.GetValue<string>(),
                    entry["resolved"]!.GetValue<string>(),
                    entry["integrity"]!.GetValue<string>(),
                    entry["optional"]?.GetValue<bool>() ?? false,
                    Strings(entry["os"]),
                    Strings(entry["cpu"]));
            }),
        ];
    }

    internal static void Write(string path, IEnumerable<LockedPackage> packages)
    {
        var map = new JsonObject();
        foreach (var package in packages.OrderBy(p => p.Path, StringComparer.Ordinal))
        {
            var entry = new JsonObject
            {
                ["version"] = package.Version,
                ["resolved"] = package.Resolved,
                ["integrity"] = package.Integrity,
            };
            if (package.Optional)
            {
                entry["optional"] = true;
            }

            if (package.Os.Count > 0)
            {
                entry["os"] = new JsonArray([.. package.Os.Select(o => (JsonNode)o)]);
            }

            if (package.Cpu.Count > 0)
            {
                entry["cpu"] = new JsonArray([.. package.Cpu.Select(c => (JsonNode)c)]);
            }

            map[package.Path] = entry;
        }

        File.WriteAllText(path, Json.Write(new JsonObject { ["lockVersion"] = 1, ["packages"] = map }));
    }

    // "node_modules/a/node_modules/@b/c" -> "@b/c"; "data/x" -> "x".
    private static string Name(string path)
    {
        var at = path.LastIndexOf("node_modules/", StringComparison.Ordinal);
        return at >= 0 ? path[(at + "node_modules/".Length)..] : path[DataPrefix.Length..];
    }

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray list ? [.. list.Select(n => n!.GetValue<string>())] : [];
}

/// <summary>JSON as the committed files hold it: two-space indent, readable text, one trailing newline.</summary>
internal static class Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string Write(JsonNode node) => node.ToJsonString(Options) + "\n";
}
