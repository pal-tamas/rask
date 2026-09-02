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

        // Said out loud rather than logged at debug, and said even when the list is empty. A pass that
        // covered a site's static half while its parameterised routes went unmentioned would read as
        // though it had covered everything.
        Console.WriteLine($"[Rask.Prerender] {plan.Paths.Count} route(s) to render, {plan.Skipped.Count} skipped");
        foreach (var skipped in plan.Skipped)
        {
            Console.WriteLine($"[Rask.Prerender]   skipped {skipped} — its path is not known without data");
        }

        // Read ONCE, before the first page is written — the shell being read is index.html, and the
        // root route's own output is index.html. Reading it per page would hand page two the merged
        // page one as its shell.
        var shell = await ReadShellAsync(outputDirectory).ConfigureAwait(false);
        if (shell is null)
        {
            Console.WriteLine(
                $"[Rask.Prerender] no boot shell at {Path.Combine(outputDirectory, ShellFileName)} — "
                + "writing whole documents, which will NOT boot the bundle");
        }

        var written = 0;
        foreach (var path in plan.Paths)
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
                Console.WriteLine($"[Rask.Prerender]   {path} threw — not written");
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
            written++;
        }

        Console.WriteLine($"[Rask.Prerender] wrote {written} page(s) to {outputDirectory}");

        // A second line, for the build rather than for a reader. The build cannot ask the filesystem
        // whether this pass produced anything: the root route's output IS index.html, which the boot
        // shell already occupies, so "a page exists" is true before the pass runs and stays true when
        // it writes nothing. Only the pass knows the count, so it says so in a form that survives a
        // grep and carries no punctuation an MSBuild condition has to escape.
        Console.WriteLine($"{SummaryPrefix}written={written} skipped={plan.Skipped.Count}");

        return written;
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
    ///     The published boot shell, or <c>null</c> when there is none to splice into.
    /// </summary>
    internal const string ShellFileName = "index.html";

    private static async Task<string?> ReadShellAsync(string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, ShellFileName);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path).ConfigureAwait(false)
            : null;
    }

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
