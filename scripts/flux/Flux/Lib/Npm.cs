using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rask.Flux;

/// <summary>
///     What the vendoring commands (icons, flags) share: npm's latest release of a package, read over HTTP from the
///     registry — no npm, no Node — its tarball checked against the registry's integrity and read in memory, and what to
///     do when the registry does not answer (leave every file as it is).
/// </summary>
/// <remarks>
///     RASK_UPSTREAM_FIXTURES names a directory of saved answers, one file per URL (<see cref="FixtureName" />, the same
///     name scripts/upstream/upstream.py gives one), which is how a follower runs with no network. A URL with no saved
///     answer is a registry that did not answer.
/// </remarks>
public static partial class Npm
{
    /// <summary>One release, as the registry describes it.</summary>
    public sealed record Release(string Name, string Version, string Tarball, string Integrity);

    /// <summary>The file a saved answer for <paramref name="url" /> is kept in.</summary>
    public static string FixtureName(string url) => NotFixtureSafe().Replace(Scheme().Replace(url, "", 1), "_");

    /// <summary>npm's latest stable release of <paramref name="name" />.</summary>
    public static async Task<Release> Latest(string name, string label)
    {
        var manifest = JsonNode.Parse(Encoding.UTF8.GetString(await Get($"https://registry.npmjs.org/{name.Replace("/", "%2f", StringComparison.Ordinal)}/latest", label)))!;
        var version = Js.Str(manifest["version"]) ?? "";
        if (!StableVersion().IsMatch(version))
        {
            throw Fail(label, $"npm's latest {name} is {version}, not a stable release");
        }

        var tarball = Js.Str(manifest["dist"]?["tarball"]);
        var integrity = Js.Str(manifest["dist"]?["integrity"]);
        if (tarball is null || integrity is null || !integrity.StartsWith("sha512-", StringComparison.Ordinal))
        {
            throw Fail(label, $"{name} {version} names no sha512 tarball");
        }

        return new Release(name, version, tarball, integrity);
    }

    /// <summary>The release's tarball, gunzipped; refused unless it hashes to the registry's integrity.</summary>
    public static async Task<byte[]> Tarball(Release release, string label)
    {
        var bytes = await Get(release.Tarball, label);
        var digest = $"sha512-{Convert.ToBase64String(SHA512.HashData(bytes))}";
        if (digest != release.Integrity)
        {
            throw Fail(label, $"{release.Tarball} is not the tarball the registry names ({digest})");
        }

        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var archive = new MemoryStream();
        await gzip.CopyToAsync(archive);
        return archive.ToArray();
    }

    /// <summary>
    ///     A ustar archive, read in place: 512-byte headers, each followed by its file rounded up to 512. Only the regular
    ///     files are decoded, as UTF-8.
    /// </summary>
    public static Dictionary<string, string> Untar(byte[] archive)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        string Field(int at, int start, int length)
        {
            var text = Encoding.UTF8.GetString(archive, at + start, length);
            var nul = text.IndexOf('\0', StringComparison.Ordinal);
            return nul >= 0 ? text[..nul] : text;
        }

        for (var at = 0; at + 512 <= archive.Length;)
        {
            var name = Field(at, 0, 100);
            if (name.Length == 0)
            {
                break;
            }

            var sizeText = Field(at, 124, 12).Trim();
            var size = sizeText.Length > 0 ? Convert.ToInt32(sizeText, 8) : 0;
            var prefix = Field(at, 345, 155);
            var path = prefix.Length > 0 ? $"{prefix}/{name}" : name;
            var type = Field(at, 156, 1);
            if (type is "0" or "")
            {
                entries[path] = Encoding.UTF8.GetString(archive, at + 512, Math.Min(size, archive.Length - at - 512));
            }

            at += 512 + ((size + 511) / 512 * 512);
        }

        return entries;
    }

    /// <summary>A failure the follower stops on: exit 1, with the label.</summary>
    public static FluxExit Fail(string label, string message) => new(1, $"{label}: {message}");

    /// <summary>What <paramref name="url" /> answers, as bytes; a saved answer under RASK_UPSTREAM_FIXTURES. No answer exits 0.</summary>
    public static async Task<byte[]> Get(string url, string label)
    {
        if (Environment.GetEnvironmentVariable("RASK_UPSTREAM_FIXTURES") is { Length: > 0 } saved)
        {
            var file = Path.Combine(saved, FixtureName(url));
            return File.Exists(file) ? await File.ReadAllBytesAsync(file) : throw Unanswered(label, url, "no saved answer");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("rask-upstream");
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri(url));
        }
        catch (HttpRequestException error)
        {
            throw Unanswered(label, url, error.HttpRequestError.ToString());
        }
        catch (TaskCanceledException)
        {
            throw Unanswered(label, url, "timed out");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                throw Unanswered(label, url, $"HTTP {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Fail(label, $"{url}: HTTP {(int)response.StatusCode}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
    }

    // A registry that is down, slow or limiting is tomorrow's run: exit 0, nothing written.
    private static FluxExit Unanswered(string label, string url, string why) =>
        new(0, $"{label}: {url} did not answer ({why}); leaving the files as they are.");

    [GeneratedRegex("^[^:]*://")]
    private static partial Regex Scheme();

    [GeneratedRegex("[^A-Za-z0-9.-]+")]
    private static partial Regex NotFixtureSafe();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex StableVersion();
}
