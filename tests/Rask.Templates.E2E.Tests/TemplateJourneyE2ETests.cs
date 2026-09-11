using Microsoft.Playwright;
using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     A scaffolded app is run and driven, on both front-end lanes.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of #1009 that a build cannot make: that the app a template produces
///         actually <b>works</b>. Building proves the code compiles and the bundler ran; it says nothing
///         about whether the bundle loads, whether the client can reach the host, or whether a dispatch
///         arrives at a C# handler.
///     </para>
///     <para>
///         The two lanes fail differently, so they are asserted differently:
///     </para>
///     <list type="bullet">
///         <item><b>SPA</b> — a static bundle the host serves. The starter dispatches a query on mount,
///         so the greeting appearing in the browser proves the bundle loaded, reached <c>/_rask</c>, and
///         a C# handler answered with a typed result.</item>
///         <item><b>Meta</b> — the framework's own Node server, with Kestrel in front. The starter page
///         is server-rendered, so it is asserted <b>with no browser at all</b>: the markup arriving over
///         plain HTTP is proof that Node rendered it and that Kestrel forwarded. The second assertion is
///         this lane's characteristic failure — an API route answered with a rendered PAGE, because the
///         request was forwarded when it should have been handled.</item>
///     </list>
///     <para>
///         Behind the front-end switch, because a journey needs the client the front-end tier builds and
///         rebuilding it here would double an already expensive gate.
///     </para>
/// </remarks>
public sealed class TemplateJourneyE2ETests(PlaywrightFixture browser) : IClassFixture<PlaywrightFixture>
{
    private const string SkipReason =
        "Template journey gate: set RASK_TEMPLATE_FRONTEND_E2E=1 (with RASK_TEMPLATE_E2E=1) to run it. "
        + "Each case scaffolds, installs, builds and then RUNS the app — minutes per template. "
        + "See scripts/run-template-e2e.sh --front-end.";

    private static bool Enabled =>
        TemplateBuildE2ETests.Enabled
        && Environment.GetEnvironmentVariable("RASK_TEMPLATE_FRONTEND_E2E") == "1";

    public static TheoryData<string> SpaTemplates() =>
        [.. TemplateSelection.Apply(SpaFramework.All.Select(f => f.Key))];

    public static TheoryData<string> MetaTemplates() =>
        [.. TemplateSelection.Apply(MetaTemplate.All.Select(f => f.Key))];

    [SkippableTheory]
    [MemberData(nameof(SpaTemplates))]
    public async Task A_spa_client_round_trips_a_dispatch_to_a_C_sharp_handler(string key)
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var app = await BuildAndRunAsync(key, TimeSpan.FromMinutes(2));
        var page = await browser.Browser.NewPageAsync();

        var failures = new List<string>();
        page.PageError += (_, error) => failures.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                failures.Add(message.Text);
            }
        };

        await page.GotoAsync(app.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // The greeting is the whole journey in one assertion: the bundle loaded and ran, it dispatched
        // over /_rask to the host, a C# handler answered, and the typed result reached the DOM. The
        // handler builds it as $"Hello, {query.Name}!" and every starter defaults the name to "world".
        await Assertions.Expect(page.GetByText("Hello, world!")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        Assert.True(
            failures.Count == 0,
            $"--template {key} reached the browser with errors:\n  {string.Join("\n  ", failures)}"
                + $"\n\nhost log:\n{app.Log}");
    }

    [SkippableTheory]
    [MemberData(nameof(MetaTemplates))]
    public async Task A_meta_front_end_is_rendered_by_node_and_served_through_Kestrel(string key)
    {
        Skip.IfNot(Enabled, SkipReason);

        // Longer: this host starts Node and waits for the framework's own server to bind before it
        // answers anything, and a cold framework server is slow.
        await using var app = await BuildAndRunAsync(key, TimeSpan.FromMinutes(4));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // No browser, deliberately. If the markup is in the response then Node produced it and Kestrel
        // forwarded the request — which is the arrangement this lane exists for, and the one nothing
        // else in the repository exercises.
        var page = await http.GetAsync(app.BaseUrl);
        var html = await page.Content.ReadAsStringAsync();

        Assert.True(
            page.IsSuccessStatusCode,
            $"--template {key} answered {(int)page.StatusCode} at /.\n{app.Log}");
        Assert.Contains("<h1", html, StringComparison.OrdinalIgnoreCase);

        // The characteristic failure of the lane: Kestrel forwards a request it should have ANSWERED,
        // so an API call comes back as a rendered page. It reads as a front-end bug, which is why it is
        // asserted by shape rather than by status.
        //
        // The probe is a route the template actually maps. An UNmapped path is not a defect here: the
        // lane ends its pipeline with MapFallback("{*path}") on purpose, exactly as the SPA lane falls
        // back to index.html, so "anything the host did not claim belongs to the front end" is the
        // contract rather than a leak. What must never happen is a mapped endpoint being shadowed.
        var api = await http.GetAsync($"{app.BaseUrl}/healthz");
        var apiBody = await api.Content.ReadAsStringAsync();

        Assert.False(
            apiBody.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || apiBody.Contains("<html", StringComparison.OrdinalIgnoreCase),
            $"--template {key}: /healthz — an endpoint this app maps on the HOST — was answered with a "
            + $"rendered page, so Kestrel forwarded what it should have handled.\nbody:\n{Trim(apiBody)}"
            + $"\n\nhost log:\n{app.Log}");

        Assert.True(
            api.IsSuccessStatusCode,
            $"--template {key}: /healthz answered {(int)api.StatusCode}.\n{app.Log}");
    }

    /// <summary>
    ///     Scaffolds the template, builds it WITH its front end, and starts the host.
    /// </summary>
    private static async Task<ScaffoldedHost> BuildAndRunAsync(string key, TimeSpan readyTimeout)
    {
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "Jny" + key.Replace("-", "", StringComparison.Ordinal);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var projectDirectory = Path.Combine(work, name);

        var result = TemplateBuildE2ETests.Scaffold(key, projectDirectory, name, version, islands: []);
        TemplateBuildE2ETests.Write(result, projectDirectory, feed);

        var projectFile = Path.Combine(projectDirectory, name + ".csproj");
        var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{projectFile}\" -m:1");

        Assert.True(
            exit == 0,
            $"--template {key} does not build with its front end:\n{CliBuildE2E.Diagnostics(output)}");

        return await ScaffoldedHost.StartAsync(projectDirectory, projectFile, readyTimeout);
    }

    private static string Trim(string body) =>
        body.Length <= 400 ? body : body[..400] + "…";
}
