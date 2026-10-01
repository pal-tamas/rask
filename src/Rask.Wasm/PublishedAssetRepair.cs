using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rask.Wasm;

/// <summary>Brings the publish output's compressed siblings and endpoint manifest back in step with pages the prerender rewrote.</summary>
internal static class PublishedAssetRepair
{
    /// <summary>
    ///     Brings the published artifacts that DESCRIBE a page back into agreement with the page.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pass runs after publish, which is the only time the fingerprinted import map exists —
    ///         and by then the SDK has already compressed the boot shell and written a manifest
    ///         describing it. Overwriting <c>index.html</c> leaves both behind: <c>index.html.br</c> and
    ///         <c>index.html.gz</c> still hold the SHELL, and the manifest still records the shell's
    ///         length, ETag and integrity.
    ///     </para>
    ///     <para>
    ///         <b>That is not cosmetic drift, it is prerendering not happening.</b> Any host that prefers
    ///         a precompressed sibling — nginx <c>brotli_static</c>, Netlify, Cloudflare Pages, S3 behind
    ///         a CDN, and Rask's own <c>MapRaskSpa</c> — serves the spinner to every visitor and
    ///         every crawler while a perfectly good prerendered page sits on disk beside it. Measured
    ///         here: a 76 KB rendered <c>index.html</c> next to a 2.3 KB <c>.br</c> of the shell, and a
    ///         manifest promising <c>Content-Length: 7292</c> for a 76,579-byte file, which is a wrong
    ///         response rather than a stale one.
    ///     </para>
    ///     <para>
    ///         Siblings are REGENERATED rather than deleted, so the bytes saved stay saved; a file with
    ///         no sibling gains none, because which assets are worth compressing is the SDK's decision
    ///         and not this pass's. Pages the pass created in new directories have no manifest entry to
    ///         repair — a manifest-driven host reaches those through its SPA fallback exactly as it did
    ///         before, so they are no worse off than un-prerendered, and adding entries for them is a
    ///         separate job from making the existing ones true.
    ///     </para>
    /// </remarks>
    internal static void RefreshPublishedArtifacts(string outputDirectory, IReadOnlyList<string> files)
    {
        var root = Path.GetFullPath(outputDirectory);
        var refreshed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            Track(root, file, refreshed);

            // Guarded on the platform rather than suppressed: BrotliStream does not exist in a
            // browser, and neither does a publish directory, so this is desktop-only in fact as well
            // as in the analyzer's model.
            if (!OperatingSystem.IsBrowser())
            {
                foreach (var sibling in RefreshSiblings(file))
                {
                    Track(root, sibling, refreshed);
                }
            }
        }

        if (refreshed.Count == 0)
        {
            return;
        }

        Console.WriteLine($"[Rask.Prerender] refreshed {refreshed.Count} published artifact(s)");
        RepairEndpointManifest(root, refreshed);
    }

    /// <summary>
    ///     Rewrites the <c>.br</c> and <c>.gz</c> siblings a file already has, and returns which ones.
    /// </summary>
    /// <remarks>
    ///     A file with no sibling gains none: which assets are worth compressing is the SDK's decision,
    ///     and inventing one here would ship a variant nothing knows about.
    /// </remarks>
    [System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
    private static IEnumerable<string> RefreshSiblings(string file)
    {
        var bytes = File.ReadAllBytes(file);

        foreach (var suffix in new[] { ".br", ".gz" })
        {
            var sibling = file + suffix;
            if (!File.Exists(sibling))
            {
                continue;
            }

            using var output = new MemoryStream();
            using (Stream compressor = string.Equals(suffix, ".br", StringComparison.Ordinal)
                       ? new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
                       : new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                compressor.Write(bytes, 0, bytes.Length);
            }

            File.WriteAllBytes(sibling, output.ToArray());
            yield return sibling;
        }
    }

    private static void Track(string root, string file, Dictionary<string, string> refreshed)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        refreshed[relative] = file;
    }

    /// <summary>
    ///     Rewrites the length, ETag, Last-Modified and integrity the endpoint manifest records for each
    ///     refreshed file.
    /// </summary>
    /// <remarks>
    ///     Best effort by design. A publish that ships no manifest is the ordinary static case and must
    ///     not fail here, and a manifest whose shape the SDK has changed is a reason to leave it alone
    ///     rather than to corrupt it — but a silent no-op is how this class of bug lives for years, so
    ///     every path that gives up says so.
    /// </remarks>
    private static void RepairEndpointManifest(string root, Dictionary<string, string> refreshed)
    {
        var publishDirectory = Path.GetDirectoryName(root);
        if (publishDirectory is null)
        {
            return;
        }

        var manifests = Directory.GetFiles(publishDirectory, "*.staticwebassets.endpoints.json");
        if (manifests.Length == 0)
        {
            return;
        }

        foreach (var manifest in manifests)
        {
            int patched;
            try
            {
                patched = PatchManifest(manifest, refreshed);
            }
            catch (JsonException error)
            {
                Console.WriteLine(
                    $"[Rask.Prerender] could not read {Path.GetFileName(manifest)} ({error.Message}) — "
                    + "a host that serves from it will describe the pre-render shell");
                continue;
            }

            Console.WriteLine(
                $"[Rask.Prerender] repaired {patched} endpoint(s) in {Path.GetFileName(manifest)}");
        }
    }

    /// <summary>What a static-web-assets endpoint says about the file it serves.</summary>
    private sealed record AssetFacts(string Length, string ETag, string Modified, string Integrity)
    {
        public static AssetFacts Of(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var hash = Convert.ToBase64String(SHA256.HashData(bytes));
            return new AssetFacts(
                bytes.Length.ToString(CultureInfo.InvariantCulture),
                $"\"{hash}\"",
                File.GetLastWriteTimeUtc(path).ToString("R", CultureInfo.InvariantCulture),
                $"sha256-{hash}");
        }
    }

    private static string UncompressedName(string asset) =>
        asset.EndsWith(".br", StringComparison.OrdinalIgnoreCase) || asset.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? asset[..^3]
            : asset;

    private static void PatchHeaders(JsonArray headers, AssetFacts facts)
    {
        foreach (var header in headers.OfType<JsonObject>())
        {
            var value = header["Name"]?.GetValue<string>() switch
            {
                "Content-Length" => facts.Length,
                "ETag" => facts.ETag,
                "Last-Modified" => facts.Modified,
                _ => null,
            };

            if (value is not null)
            {
                header["Value"] = value;
            }
        }
    }

    private static void PatchIntegrity(JsonArray properties, string integrity)
    {
        foreach (var property in properties.OfType<JsonObject>()
                     .Where(p => string.Equals(p["Name"]?.GetValue<string>(), "integrity", StringComparison.Ordinal)))
        {
            property["Value"] = integrity;
        }
    }

    private static int PatchManifest(string manifest, Dictionary<string, string> refreshed)
    {
        var document = JsonNode.Parse(File.ReadAllText(manifest));
        if (document?["Endpoints"] is not JsonArray endpoints)
        {
            return 0;
        }

        var described = new Dictionary<string, AssetFacts>(StringComparer.OrdinalIgnoreCase);

        var patched = 0;
        foreach (var endpoint in endpoints)
        {
            var asset = endpoint?["AssetFile"]?.GetValue<string>()?.Replace('\\', '/');
            if (asset is null || !refreshed.TryGetValue(asset, out var path))
            {
                continue;
            }

            if (!described.TryGetValue(asset, out var facts))
            {
                facts = AssetFacts.Of(path);
                described[asset] = facts;
            }

            if (endpoint?["ResponseHeaders"] is JsonArray headers)
            {
                PatchHeaders(headers, facts);
            }

            // The integrity an endpoint advertises is the UNCOMPRESSED asset's, so a compressed variant
            // carries the hash of the file it decompresses to rather than its own bytes.
            if (endpoint?["EndpointProperties"] is JsonArray properties
                && described.TryGetValue(UncompressedName(asset), out var origin))
            {
                PatchIntegrity(properties, origin.Integrity);
            }

            patched++;
        }

        if (patched > 0)
        {
            File.WriteAllText(manifest, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        return patched;
    }
}
