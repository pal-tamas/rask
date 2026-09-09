using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Wasm;

/// <summary>
///     Writes an app's pages to disk at publish time, for an app that has no server to render them.
/// </summary>
/// <remarks>
///     <para>
///         A browser-WebAssembly app published to a static host serves its boot shell to everything
///         that asks: a spinner, and the word "Loading". The real markup does not exist until several
///         megabytes of runtime have downloaded, so that is what a crawler indexes and what a social
///         card previews.
///     </para>
///     <para>
///         <b>Driven from the app's own <c>Program.cs</c>, deliberately.</b> The alternative — a
///         generated entry point compiled without <c>Program.cs</c> — cannot work, because that file is
///         where the app registers its services, and a page that injects anything would find nothing
///         registered. Reusing the real entry point means the services are exactly the ones the app
///         configured, with no second place to keep in sync and no hook to learn.
///     </para>
/// </remarks>
public static class WasmPrerender
{
    /// <summary>
    ///     The directory to write into, set by the build. Absent outside a prerender pass.
    /// </summary>
    /// <remarks>
    ///     An environment variable rather than a non-browser compile check: <c>Rask.Wasm</c> also
    ///     targets <c>net10.0</c> for its own tests, and those call <c>RunAsync</c> expecting a boot.
    ///     Prerendering has to be asked for, not inferred from the target framework.
    /// </remarks>
    public const string OutputVariable = "RASK_PRERENDER_OUT";

    internal static string? RequestedOutput => Environment.GetEnvironmentVariable(OutputVariable);

    /// <summary>
    ///     The URL prefix the bundle will be published under, set by the build from
    ///     <c>$(RaskPathBase)</c>.
    /// </summary>
    /// <remarks>
    ///     A browser boot reads this off the document's <c>&lt;base href&gt;</c>; a prerender pass has no
    ///     document, so the build has to say. Without it every <c>LiveOptions.PathBase</c>-prefixed URL
    ///     is baked against an empty prefix — root-relative, which a <c>&lt;base href&gt;</c> cannot
    ///     redirect because it only applies to relative URLs. A sub-path deploy then serves pages that
    ///     ask the origin root for their own scoped assets.
    /// </remarks>
    public const string PathBaseVariable = "RASK_PRERENDER_PATH_BASE";

    /// <summary>
    ///     The absolute origin the bundle will be served from, set by the build from
    ///     <c>$(RaskSiteUrl)</c>. Absent unless the app says so.
    /// </summary>
    /// <remarks>
    ///     A sitemap lists <b>absolute</b> URLs — that is not a style choice, it is what the protocol
    ///     says, and a crawler discards a sitemap of relative paths. Nothing else in a publish knows the
    ///     origin: the bundle is static files, and the same files are correct on a preview host, a
    ///     staging domain and production. So the app has to name it, and when it does not, this pass
    ///     writes no sitemap and says why rather than guessing a domain into a published file.
    /// </remarks>
    public const string SiteUrlVariable = "RASK_PRERENDER_SITE_URL";

    /// <summary>
    ///     Whether the published URLs end in a slash, set by the build from
    ///     <c>$(RaskSiteTrailingSlash)</c>. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pass writes <c>{route}/index.html</c>, and which URL serves that file WITHOUT a
    ///         redirect is the host's decision, not the build's. GitHub Pages answers <c>/docs</c> with a
    ///         301 to <c>/docs/</c>; Netlify and Cloudflare Pages do the opposite and strip the slash.
    ///         Whichever way the host goes, naming the other form in a canonical or a sitemap points
    ///         every URL at a redirect — which Search Console reports as "page with redirect", and which
    ///         is worse than it sounds when the page it redirects to declares the redirecting URL as its
    ///         canonical. That is a contradiction, not a hop.
    ///     </para>
    ///     <para>
    ///         The default follows the file the pass actually wrote: a directory with an index in it is
    ///         served at the trailing-slash URL, which is what GitHub Pages, Jekyll, Hugo and an nginx
    ///         <c>try_files $uri $uri/</c> all do. Set the property to <c>false</c> on a host that
    ///         normalises the other way.
    ///     </para>
    /// </remarks>
    public const string TrailingSlashVariable = "RASK_PRERENDER_TRAILING_SLASH";

    /// <summary>
    ///     Renders every prerenderable route into <paramref name="outputDirectory" />.
    /// </summary>
    /// <returns>How many pages were written.</returns>
    internal static async Task<int> RunAsync<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        IServiceProvider services,
        string outputDirectory,
        TimeSpan budget)
        where TApp : Component
    {
        ApplyPathBase();

        var plan = RaskPrerender.PlanRoutes();

        // The FILES, as opposed to the routes: what the refresh step at the end has to bring the
        // compressed siblings and the endpoint manifest back into agreement with.
        var writtenFiles = new List<string>();

        // The literal routes, plus whatever the app says its parameterised ones expand to. A docs site's
        // /guides/{slug} is one route and eighty pages, and the pass cannot know the slugs — so without
        // this the whole of a site's actual content ships as an empty boot shell while the publish
        // reports a healthy count of the pages around it.
        var supplied = SuppliedPaths(services, plan);
        var paths = supplied.Count == 0 ? plan.Paths : [.. plan.Paths, .. supplied];

        // Said out loud rather than logged at debug, and said even when the list is empty. A pass that
        // covered a site's static half while its parameterised routes went unmentioned would read as
        // though it had covered everything.
        Console.WriteLine(
            $"[Rask.Prerender] {paths.Count} route(s) to render, {plan.Skipped.Count} skipped");
        if (supplied.Count > 0)
        {
            Console.WriteLine(
                $"[Rask.Prerender]   {supplied.Count} of them supplied by IPrerenderPaths");
        }

        foreach (var skipped in plan.Skipped)
        {
            Console.WriteLine(
                $"[Rask.Prerender]   skipped {skipped} — its path is not known without data"
                + " (register an IPrerenderPaths to supply them)");
        }

        // Read ONCE, before the first page is written — the shell being read is index.html, and the
        // root route's own output is index.html. Reading it per page would hand page two the merged
        // page one as its shell.
        //
        // The same argument holds ACROSS runs, and reading once does not answer it: a second publish
        // into the same directory finds the FIRST publish's merged page under that name. ReadShellAsync
        // is where that case is detected and recovered from (#1036).
        var shell = await ReadShellAsync(outputDirectory).ConfigureAwait(false);
        if (shell is null)
        {
            Console.WriteLine(
                $"[Rask.Prerender] no boot shell at {Path.Combine(outputDirectory, ShellFileName)} — "
                + "writing whole documents, which will NOT boot the bundle");
        }

        // The untouched shell, kept where a static host will serve it for an unknown path (#974).
        //
        // Prerendering an app with un-prerenderable routes used to break their deep links, and by
        // building the very thing that was supposed to help. The root route's own output IS index.html,
        // so once this pass runs the file a static host falls back to is no longer a neutral shell —
        // it is the HOME PAGE, fully rendered. A deep link to a route that could not be prerendered
        // then arrives, gets the home page's markup, and the bundle boots into a document already
        // describing a different page. Before prerendering, the same link got an empty shell and
        // routed correctly.
        //
        // 404.html because that is what every static host this targets already reaches for — GitHub
        // Pages, Netlify, Cloudflare Pages, S3 — and it costs no configuration. Written BEFORE the loop,
        // from the shell read above, so it cannot pick up a page's output.
        if (shell is not null)
        {
            var fallback = Path.Combine(outputDirectory, FallbackFileName);
            await File.WriteAllTextAsync(fallback, shell).ConfigureAwait(false);
            writtenFiles.Add(fallback);
            Console.WriteLine($"[Rask.Prerender] wrote the neutral boot shell to {FallbackFileName}");
        }

        // The paths the SITEMAP should list, which is neither plan.Paths nor "everything written". A
        // route that threw or timed out is not written at all, and listing it would send a crawler to a
        // URL answering with the boot shell — the thing prerendering exists to stop it seeing. A route
        // that renders a noindex page IS written, and must still not be listed.
        var writtenPaths = new List<string>(paths.Count);
        var written = 0;
        foreach (var path in paths)
        {
            // A scope per page, as a request would get: a page that injects something scoped must not
            // see the previous page's instance.
            using var scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<RouteState>().Path = path;

            var app = ActivatorUtilities.CreateInstance<TApp>(scope.ServiceProvider);
            var result = await RaskPrerender
                .RenderDocumentAsync(app, scope.ServiceProvider, budget)
                .ConfigureAwait(false);

            // Both of these still hand back perfectly ordinary HTML — an error document, or the
            // placeholder that was on screen when the budget ran out. Writing either would publish it
            // under the route's own name, and a baked spinner is worse than no prerender at all because
            // it looks prerendered. Skip and say so; the bundle still serves the route at runtime.
            if (result.Faulted)
            {
                // WHAT threw, not merely that something did. A skipped page is a URL that ships as the
                // boot shell — correct for a visitor, blank for a crawler — so this line is the only
                // notice anyone gets, and "threw" on its own sends the reader to guess. Type and
                // message, plus the innermost cause, which for a DI failure is where the name is.
                Console.WriteLine($"[Rask.Prerender]   {path} threw — not written: {Describe(result.Error)}");
                continue;
            }

            if (result.TimedOut)
            {
                Console.WriteLine($"[Rask.Prerender]   {path} did not settle in {budget.TotalSeconds:0.#}s — not written");
                continue;
            }

            // Spliced into the shell rather than written over it. The shell carries the fingerprinted
            // import map, the SRI-pinned preload, the <base href> and the script that boots the bundle;
            // replacing the file would publish real markup that can never become interactive.
            var html = shell is null ? result.Html : PrerenderShell.Merge(shell, result.Html);

            var file = OutputPathFor(outputDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, html).ConfigureAwait(false);
            writtenFiles.Add(file);
            written++;

            // Written, but not necessarily LISTED. Both reasons are read off the page's own rendered
            // markup rather than from a second declaration, so the page and the sitemap cannot disagree.
            //
            //   * noindex — the page has asked not to be in search results, and a sitemap is a request
            //     to index. Listing it submits a contradiction, which Search Console reports as an error
            //     against the whole file rather than against the one URL.
            //   * a canonical pointing SOMEWHERE ELSE — the page has said another URL is the real one.
            //     An add form that canonicalises to its list is the ordinary case, and a sitemap lists
            //     canonical URLs; listing both asks a crawler to index a page that disclaims itself.
            if (ListedInSitemap(html, path))
            {
                writtenPaths.Add(path);
            }
        }

        Console.WriteLine($"[Rask.Prerender] wrote {written} page(s) to {outputDirectory}");

        // A second line, for the build rather than for a reader. The build cannot ask the filesystem
        // whether this pass produced anything: the root route's output IS index.html, which the boot
        // shell already occupies, so "a page exists" is true before the pass runs and stays true when
        // it writes nothing. Only the pass knows the count, so it says so in a form that survives a
        // grep and carries no punctuation an MSBuild condition has to escape.
        WriteSitemap(outputDirectory, writtenPaths, writtenFiles);

        // Last, because it reads back every file the pass wrote — including the sitemap and robots.txt
        // above, which a static host compresses just like a page.
        RefreshPublishedArtifacts(outputDirectory, writtenFiles);

        Console.WriteLine($"{SummaryPrefix}written={written} skipped={plan.Skipped.Count}");

        return written;
    }

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
    ///         a CDN, and Rask's own <c>Rask.Wasm.Hosting</c> — serves the spinner to every visitor and
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
    private static void RefreshPublishedArtifacts(string outputDirectory, IReadOnlyList<string> files)
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
            using (Stream compressor = suffix == ".br"
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

    private static int PatchManifest(string manifest, Dictionary<string, string> refreshed)
    {
        var document = JsonNode.Parse(File.ReadAllText(manifest));
        if (document?["Endpoints"] is not JsonArray endpoints)
        {
            return 0;
        }

        var described = new Dictionary<string, (string Length, string ETag, string Modified, string Integrity)>(
            StringComparer.OrdinalIgnoreCase);

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
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToBase64String(SHA256.HashData(bytes));
                facts = (
                    bytes.Length.ToString(CultureInfo.InvariantCulture),
                    $"\"{hash}\"",
                    File.GetLastWriteTimeUtc(path).ToString("R", CultureInfo.InvariantCulture),
                    $"sha256-{hash}");
                described[asset] = facts;
            }

            if (endpoint?["ResponseHeaders"] is JsonArray headers)
            {
                foreach (var header in headers)
                {
                    var value = header?["Name"]?.GetValue<string>() switch
                    {
                        "Content-Length" => facts.Length,
                        "ETag" => facts.ETag,
                        "Last-Modified" => facts.Modified,
                        _ => null,
                    };

                    if (value is not null && header is not null)
                    {
                        header["Value"] = value;
                    }
                }
            }

            // The integrity an endpoint advertises is the UNCOMPRESSED asset's, so a compressed variant
            // carries the hash of the file it decompresses to rather than its own bytes.
            if (endpoint?["EndpointProperties"] is JsonArray properties)
            {
                var source = asset.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                             || asset.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? asset[..^3]
                    : asset;

                if (described.TryGetValue(source, out var origin))
                {
                    foreach (var property in properties)
                    {
                        if (property?["Name"]?.GetValue<string>() == "integrity" && property is not null)
                        {
                            property["Value"] = origin.Integrity;
                        }
                    }
                }
            }

            patched++;
        }

        if (patched > 0)
        {
            File.WriteAllText(manifest, document!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        return patched;
    }

    /// <summary>
    ///     Writes <c>sitemap.xml</c> for the pages that reached disk, and a <c>robots.txt</c> pointing at
    ///     it, when the app has named the origin it will be served from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built from what was WRITTEN rather than from the route table, because those are different
    ///         lists and the difference is the whole point: a route the pass skipped still answers, but
    ///         with the boot shell, and a sitemap is a promise that the URL has content.
    ///     </para>
    ///     <para>
    ///         An existing <c>robots.txt</c> is never overwritten. It is a file with real consequences —
    ///         a wrong one delists a site — so an author who shipped one has said something this pass has
    ///         no business editing, and it says how to add the sitemap line instead.
    ///     </para>
    /// </remarks>
    private static void WriteSitemap(
        string outputDirectory, IReadOnlyList<string> paths, List<string> writtenFiles)
    {
        var origin = Environment.GetEnvironmentVariable(SiteUrlVariable)?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(origin))
        {
            // Said out loud. A missing sitemap is invisible in a browser and would be invisible in the
            // build output too, and "we shipped for months without one" is how that ends. Not a warning:
            // an app with no fixed origin is a legitimate configuration, and guessing a domain into a
            // published file is worse than shipping no sitemap.
            Console.WriteLine(
                "[Rask.Prerender] no sitemap — set <RaskSiteUrl>https://example.com</RaskSiteUrl> to "
                + "publish one (a sitemap carries absolute URLs, so the origin cannot be inferred)");
            return;
        }

        if (paths.Count == 0)
        {
            Console.WriteLine("[Rask.Prerender] no sitemap — no page was written");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        // Off only when the app says its host strips the slash; see TrailingSlashVariable.
        var trailingSlash = !string.Equals(
            Environment.GetEnvironmentVariable(TrailingSlashVariable)?.Trim(),
            "false",
            StringComparison.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            // LiveOptions.PathBase is already on every rendered link; it belongs here too, or a
            // sub-path deploy publishes a sitemap pointing at the origin root.
            var url = origin + LiveOptions.PathBase + SiteUrlPath(path, trailingSlash);
            builder.Append("  <url><loc>").Append(XmlEscape(url)).AppendLine("</loc></url>");
        }

        builder.AppendLine("</urlset>");

        var sitemap = Path.Combine(outputDirectory, SitemapFileName);
        File.WriteAllText(sitemap, builder.ToString());
        writtenFiles.Add(sitemap);
        Console.WriteLine($"[Rask.Prerender] wrote {SitemapFileName} with {paths.Count} URL(s)");

        var sitemapUrl = $"{origin}{LiveOptions.PathBase}/{SitemapFileName}";
        var robots = Path.Combine(outputDirectory, RobotsFileName);

        // "Existing" is not the same question as "the app's own" — the second publish into a directory
        // finds the FIRST publish's robots.txt sitting there, and calling that one the app's leaves it
        // frozen at whatever origin the earlier run was given. Same family of bug as the shell (#1036),
        // with a quieter symptom: an app that changes RaskSiteUrl keeps publishing a robots.txt
        // pointing a crawler at the old domain's sitemap, and the log says the author asked for it.
        if (File.Exists(robots) && !IsOwnRobots(File.ReadAllText(robots)))
        {
            Console.WriteLine(
                $"[Rask.Prerender] kept the app's own {RobotsFileName} — add "
                + $"\"Sitemap: {sitemapUrl}\" to it yourself");
            return;
        }

        File.WriteAllText(robots, RobotsFor(sitemapUrl));
        writtenFiles.Add(robots);
        Console.WriteLine($"[Rask.Prerender] wrote {RobotsFileName}");
    }

    /// <summary>The whole of the <c>robots.txt</c> this pass writes, for a given sitemap URL.</summary>
    private static string RobotsFor(string sitemapUrl) =>
        $"User-agent: *\nAllow: /\nSitemap: {sitemapUrl}\n";

    /// <summary>
    ///     Whether a <c>robots.txt</c> is one an earlier run of this pass wrote, rather than the app's.
    /// </summary>
    /// <remarks>
    ///     Matched on the exact three lines it writes, for ANY sitemap URL — so a file an author edited
    ///     even slightly is the author's, and stays untouched. The bar is deliberately that high:
    ///     getting this wrong overwrites a file that can delist a site.
    /// </remarks>
    private static bool IsOwnRobots(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        return lines.Length == 4
               && lines[0] == "User-agent: *"
               && lines[1] == "Allow: /"
               && lines[2].StartsWith("Sitemap: ", StringComparison.Ordinal)
               && lines[3].Length == 0;
    }

    /// <summary>
    ///     A route path in the form the host serves without redirecting.
    /// </summary>
    /// <remarks>
    ///     The root is always <c>/</c> — it is already a directory URL, and doubling the slash would
    ///     name a different resource.
    /// </remarks>
    internal static string SiteUrlPath(string path, bool trailingSlash)
    {
        var trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/";
        }

        return trailingSlash ? trimmed + "/" : trimmed;
    }

    /// <summary>Escapes the five XML entities. A route template cannot contain them today; a route is
    /// author-written text, and a sitemap that silently stops parsing is not worth the assumption.</summary>
    private static string XmlEscape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    /// <summary>
    ///     The extra paths the app's <see cref="IPrerenderPaths" /> registrations name, minus anything
    ///     the route plan already covers.
    /// </summary>
    /// <remarks>
    ///     Deduped against the literal plan AND against itself, because two registrations naming the
    ///     same page is a mistake with no error attached: the second render simply overwrites the first,
    ///     and the only trace is a route counted twice in the log and listed twice in the sitemap.
    /// </remarks>
    private static List<string> SuppliedPaths(IServiceProvider services, PrerenderPlan plan)
    {
        var sources = services.GetServices<IPrerenderPaths>().ToList();
        if (sources.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(plan.Paths, StringComparer.Ordinal);
        var extra = new List<string>();

        foreach (var source in sources)
        {
            foreach (var path in source.Paths())
            {
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                {
                    extra.Add(path);
                }
            }
        }

        return extra;
    }

    /// <summary>Whether a written page belongs in the sitemap.</summary>
    internal static bool ListedInSitemap(string html, string routePath) =>
        !IsNoIndex(html) && (CanonicalTarget(html) is not { } canonical || SamePage(canonical, routePath));

    /// <summary>The <c>href</c> of the document's canonical link, or <c>null</c> when it declares none.</summary>
    internal static string? CanonicalTarget(string html)
    {
        var rel = html.IndexOf("rel=\"canonical\"", StringComparison.OrdinalIgnoreCase);
        if (rel < 0)
        {
            return null;
        }

        // The tag's bounds, so an href from a NEIGHBOURING link cannot be read as this one's. `rel` may
        // come before or after `href` — Link writes href first today, and that is the serializer's
        // business, not this reader's.
        var open = html.LastIndexOf('<', rel);
        var close = html.IndexOf('>', rel);
        if (open < 0 || close < 0)
        {
            return null;
        }

        var tag = html[open..close];
        const string Needle = "href=\"";
        var href = tag.IndexOf(Needle, StringComparison.OrdinalIgnoreCase);
        if (href < 0)
        {
            return null;
        }

        href += Needle.Length;
        var end = tag.IndexOf('"', href);
        return end < 0 ? null : tag[href..end];
    }

    /// <summary>Whether a canonical URL names the route it was rendered for.</summary>
    /// <remarks>
    ///     Compared on the PATH, because the canonical is absolute and the route is not, and the origin
    ///     is the app's to choose. A trailing slash is not a difference: a static host serves
    ///     <c>/docs/</c> and <c>/docs</c> as the same document, and a page that spells its canonical the
    ///     other way has not said anything about a different page.
    /// </remarks>
    private static bool SamePage(string canonical, string routePath)
    {
        var path = Uri.TryCreate(canonical, UriKind.Absolute, out var uri) ? uri.AbsolutePath : canonical;
        var expected = LiveOptions.PathBase + routePath;

        return string.Equals(path.TrimEnd('/'), expected.TrimEnd('/'), StringComparison.Ordinal);
    }

    /// <summary>Whether the rendered document asks robots not to index it.</summary>
    /// <remarks>
    ///     A deliberately narrow reader: the <c>content</c> of a <c>&lt;meta name="robots"&gt;</c>, looked
    ///     at for the word <c>noindex</c>. It is checked against the DOCUMENT rather than against a
    ///     separate declaration because a page that says one thing in its head and another in a build
    ///     configuration is the failure this avoids, not one it should be able to express.
    /// </remarks>
    internal static bool IsNoIndex(string html)
    {
        var robots = html.IndexOf("name=\"robots\"", StringComparison.OrdinalIgnoreCase);
        if (robots < 0)
        {
            return false;
        }

        var tagEnd = html.IndexOf('>', robots);
        var tag = tagEnd < 0 ? html[robots..] : html[robots..tagEnd];
        return tag.Contains("noindex", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One line naming an exception and its innermost cause.</summary>
    private static string Describe(Exception? error)
    {
        if (error is null)
        {
            // The boundary reported a fault without an exception. Not reachable today, and said out
            // loud rather than printed as an empty string, which would read like a truncated line.
            return "(the root boundary reported a fault but carried no exception)";
        }

        var text = $"{error.GetType().Name}: {error.Message}";

        var inner = error;
        while (inner.InnerException is { } next)
        {
            inner = next;
        }

        return ReferenceEquals(inner, error) ? text : $"{text} -> {inner.GetType().Name}: {inner.Message}";
    }

    /// <summary>
    ///     Marks the machine-readable summary line. Read by <c>RaskPrerenderPages</c>.
    /// </summary>
    internal const string SummaryPrefix = "[Rask.Prerender] result ";

    /// <summary>
    ///     Seeds <see cref="LiveOptions.PathBase" /> from the build, for the pages about to render.
    /// </summary>
    /// <remarks>
    ///     Only when nothing has set it already: a host that was configured with an explicit
    ///     <c>PathBase</c> in <c>Program.cs</c> has said something more specific than the publish flag,
    ///     and this must not overrule it. That matches the browser boot, where an explicit value also
    ///     wins over the <c>&lt;base href&gt;</c> auto-detect.
    /// </remarks>
    internal static void ApplyPathBase()
    {
        if (LiveOptions.PathBase.Length > 0)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable(PathBaseVariable) is not { Length: > 0 } raw)
        {
            return;
        }

        var normalized = RaskPath.Normalize(raw);
        if (normalized.Length == 0)
        {
            return;
        }

        LiveOptions.PathBase = normalized;
        Console.WriteLine($"[Rask.Prerender] rendering under path base {normalized}");
    }

    /// <summary>
    ///     Where the SDK publishes the boot shell — and, because the root route's output lands on the
    ///     same name, where this pass's own home page ends up. See <see cref="ReadShellAsync" />.
    /// </summary>
    internal const string ShellFileName = "index.html";

    /// <summary>
    ///     Where the UNTOUCHED shell is kept, so a deep link to a route this pass could not prerender
    ///     still gets a neutral document to boot into rather than the home page's markup (#974).
    /// </summary>
    internal const string FallbackFileName = "404.html";

    /// <summary>The sitemap the pass writes beside the pages, when the app names its origin.</summary>
    internal const string SitemapFileName = "sitemap.xml";

    /// <summary>Written only when the app ships none of its own.</summary>
    internal const string RobotsFileName = "robots.txt";

    /// <summary>
    ///     The published boot shell, read from <see cref="ShellFileName" /> — or, when that file is this
    ///     pass's own output from an earlier publish, from the untouched copy at
    ///     <see cref="FallbackFileName" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Idempotence (#1036).</b> The pass runs <c>AfterTargets="Publish"</c> and writes into
    ///         the published <c>wwwroot</c>, where the root route's own output IS <c>index.html</c> —
    ///         the file the shell is read from. Publish twice into the same directory and the SDK does
    ///         not rescue it: the prerendered <c>index.html</c> is NEWER than the staged shell, so the
    ///         copy step calls it up to date and leaves it alone. The second pass then reads a merged
    ///         page as its shell and splices the head into a document that already has it, and a third
    ///         publish makes three of everything. Silently — the build is green and the page renders.
    ///     </para>
    ///     <para>
    ///         The pass already keeps what it needs to recover: it writes the untouched shell to
    ///         <c>404.html</c> before the page loop (#974), so a deep link to an un-prerenderable route
    ///         still gets a neutral document. That file is the pristine shell by construction — written
    ///         from this same value, before anything is merged — so it is exactly the input the second
    ///         run wants.
    ///     </para>
    ///     <para>
    ///         And when there is no pristine copy to fall back on, this THROWS rather than merging into
    ///         output. A silent duplication is the thing being fixed, and quietly writing whole
    ///         documents instead would publish pages that can never boot. The remedy is one line, and
    ///         the message says it.
    ///     </para>
    /// </remarks>
    private static async Task<string?> ReadShellAsync(string outputDirectory)
    {
        var shell = await ReadIfPresentAsync(Path.Combine(outputDirectory, ShellFileName))
            .ConfigureAwait(false);

        if (shell is null || !PrerenderShell.IsRendered(shell))
        {
            return shell;
        }

        var pristine = await ReadIfPresentAsync(Path.Combine(outputDirectory, FallbackFileName))
            .ConfigureAwait(false);

        if (pristine is not null && !PrerenderShell.IsRendered(pristine))
        {
            Console.WriteLine(
                $"[Rask.Prerender] {ShellFileName} is a page an earlier publish rendered, not a boot "
                + $"shell — reading the untouched shell from {FallbackFileName} instead");
            return pristine;
        }

        throw new InvalidOperationException(
            $"Rask prerendering found a rendered page at {Path.Combine(outputDirectory, ShellFileName)} "
            + $"and no untouched boot shell at {FallbackFileName} to read instead. Merging into it would "
            + "append a second copy of every head asset — every stylesheet, preload, meta and canonical. "
            + "Delete the publish directory and publish again.");
    }

    private static async Task<string?> ReadIfPresentAsync(string path) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : null;

    /// <summary>
    ///     Where a route's document goes: <c>/</c> is the directory's own <c>index.html</c>, and
    ///     <c>/about</c> is <c>about/index.html</c>.
    /// </summary>
    /// <remarks>
    ///     Directory-per-route rather than <c>about.html</c>, so a static host serves the page at the
    ///     URL the app routes to — with no extension in it, and no per-host rewrite rule to configure.
    /// </remarks>
    internal static string OutputPathFor(string root, string routePath)
    {
        var relative = routePath.Trim('/');
        return relative.Length == 0
            ? Path.Combine(root, "index.html")
            : Path.Combine(root, Path.Combine(relative.Split('/')), "index.html");
    }
}
