using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using static Rask.Wasm.PrerenderedPageMetadata;
using static Rask.Wasm.PublishedAssetRepair;
using static Rask.Wasm.SitemapWriter;

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

        var paths = PathsToRender(services, plan);

        // Read ONCE, before the first page is written — the shell being read is index.html, and the
        // root route's own output is index.html. Reading it per page would hand page two the merged
        // page one as its shell.
        //
        // The same argument holds ACROSS runs, and reading once does not answer it: a second publish
        // into the same directory finds the FIRST publish's merged page under that name. ReadShellAsync
        // is where that case is detected and recovered from (#1036).
        var shell = await ReadShellAsync(outputDirectory).ConfigureAwait(false);
        await WriteFallbackAsync(outputDirectory, shell, writtenFiles).ConfigureAwait(false);

        // The paths the SITEMAP should list, which is neither plan.Paths nor "everything written". A
        // route that threw or timed out is not written at all, and listing it would send a crawler to a
        // URL answering with the boot shell — the thing prerendering exists to stop it seeing. A route
        // that renders a noindex page IS written, and must still not be listed.
        var writtenPaths = new List<SitemapEntry>(paths.Count);
        var written = 0;
        var trailingSlash = HostServesTrailingSlash();
        foreach (var path in paths)
        {
            if (await RenderPageAsync<TApp>(services, path, trailingSlash, budget).ConfigureAwait(false) is not { } rendered)
            {
                continue;
            }

            var html = await WritePageAsync(outputDirectory, path, shell, rendered, writtenFiles).ConfigureAwait(false);
            written++;

            if (SitemapEntryFor(html, path) is { } entry)
            {
                writtenPaths.Add(entry);
            }
        }

        Console.WriteLine($"[Rask.Prerender] wrote {written} page(s) to {outputDirectory}");

        WriteSitemap(outputDirectory, writtenPaths, writtenFiles);

        // Last, because it reads back every file the pass wrote — including the sitemap and robots.txt
        // above, which a static host compresses just like a page.
        RefreshPublishedArtifacts(outputDirectory, writtenFiles);

        ReportForTheBuild(written, plan.Skipped.Count);
        return written;
    }

    // A second line, for the build rather than for a reader. The build cannot ask the filesystem
    // whether this pass produced anything: the root route's output IS index.html, which the boot
    // shell already occupies, so "a page exists" is true before the pass runs and stays true when
    // it writes nothing. Only the pass knows the count, so it says so in a form that survives a
    // grep and carries no punctuation an MSBuild condition has to escape.
    private static void ReportForTheBuild(int written, int skipped) =>
        Console.WriteLine($"{SummaryPrefix}written={written} skipped={skipped}");

    private static async Task<string> WritePageAsync(
        string outputDirectory, string path, string? shell, string rendered, List<string> writtenFiles)
    {
        // Spliced into the shell rather than written over it. The shell carries the fingerprinted
        // import map, the SRI-pinned preload, the <base href> and the script that boots the bundle, so
        // replacing the file would publish real markup that can never become interactive.
        var html = shell is null ? rendered : PrerenderShell.Merge(shell, rendered);

        var file = OutputPathFor(outputDirectory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, html).ConfigureAwait(false);
        writtenFiles.Add(file);
        return html;
    }

    // Written, but not necessarily LISTED. Both reasons are read off the page's own rendered
    // markup rather than from a second declaration, so the page and the sitemap cannot disagree.
    //
    //   * noindex — the page has asked not to be in search results, and a sitemap is a request
    //     to index. Listing it submits a contradiction, which Search Console reports as an error
    //     against the whole file rather than against the one URL.
    //   * a canonical pointing SOMEWHERE ELSE — the page has said another URL is the real one.
    //     An add form that canonicalises to its list is the ordinary case, and a sitemap lists
    //     canonical URLs; listing both asks a crawler to index a page that disclaims itself.
    //
    // And the date it last changed comes off the same markup, for the same reason: see LastModifiedOf.
    private static SitemapEntry? SitemapEntryFor(string html, string path) =>
        ListedInSitemap(html, path) ? new SitemapEntry(path, LastModifiedOf(html)) : null;

    // The literal routes, plus whatever the app says its parameterised ones expand to. A docs site's
    // /guides/{slug} is one route and eighty pages, and the pass cannot know the slugs — so without
    // this the whole of a site's actual content ships as an empty boot shell while the publish
    // reports a healthy count of the pages around it.
    private static IReadOnlyList<string> PathsToRender(IServiceProvider services, PrerenderPlan plan)
    {
        var supplied = SuppliedPaths(services, plan);
        IReadOnlyList<string> paths = supplied.Count == 0 ? plan.Paths : [.. plan.Paths, .. supplied];
        AnnouncePlan(plan, paths.Count, supplied.Count);
        return paths;
    }

    // Said out loud rather than logged at debug, and said even when the list is empty. A pass that
    // covered a site's static half while its parameterised routes went unmentioned would read as
    // though it had covered everything.
    private static void AnnouncePlan(PrerenderPlan plan, int routes, int supplied)
    {
        Console.WriteLine($"[Rask.Prerender] {routes} route(s) to render, {plan.Skipped.Count} skipped");
        if (supplied > 0)
        {
            Console.WriteLine($"[Rask.Prerender]   {supplied} of them supplied by IPrerenderPaths");
        }

        foreach (var skipped in plan.Skipped)
        {
            Console.WriteLine(
                $"[Rask.Prerender]   skipped {skipped} — its path is not known without data"
                + " (register an IPrerenderPaths to supply them)");
        }
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
    private static async Task WriteFallbackAsync(string outputDirectory, string? shell, List<string> writtenFiles)
    {
        if (shell is null)
        {
            Console.WriteLine(
                $"[Rask.Prerender] no boot shell at {Path.Combine(outputDirectory, ShellFileName)} — "
                + "writing whole documents, which will NOT boot the bundle");
            return;
        }

        var fallback = Path.Combine(outputDirectory, FallbackFileName);
        await File.WriteAllTextAsync(fallback, shell).ConfigureAwait(false);
        writtenFiles.Add(fallback);
        Console.WriteLine($"[Rask.Prerender] wrote the neutral boot shell to {FallbackFileName}");
    }

    // The page's markup, or null — having said why — when it must not be written.
    private static async Task<string?> RenderPageAsync<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        IServiceProvider services, string path, bool trailingSlash, TimeSpan budget)
        where TApp : Component
    {
        // A scope per page, as a request would get: a page that injects something scoped must not
        // see the previous page's instance.
        using var scope = services.CreateScope();

        // The path the BROWSER will report for this file, not the route template's spelling. The page
        // is written to {route}/index.html, which GitHub Pages serves at /docs/ — so once the bundle
        // boots, RouteState.Path is "/docs/". Seeding the bare "/docs" here baked a document that
        // disagreed with its own hydrated render, and anything that prints or compares the path
        // (a breadcrumb, a "path:" badge) visibly jumped the moment the runtime took over.
        scope.ServiceProvider.GetRequiredService<RouteState>().Path = SiteUrlPath(path, trailingSlash);

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
            return null;
        }

        if (result.TimedOut)
        {
            Console.WriteLine($"[Rask.Prerender]   {path} did not settle in {budget.TotalSeconds:0.#}s — not written");
            return null;
        }

        return result.Html;
    }

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
            extra.AddRange(source.Paths().Where(path => !string.IsNullOrEmpty(path) && seen.Add(path)));
        }

        return extra;
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
            : Path.Combine([root, .. relative.Split('/'), "index.html"]);
    }
}
