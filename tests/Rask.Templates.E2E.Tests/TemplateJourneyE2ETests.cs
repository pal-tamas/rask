using Microsoft.Playwright;
using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     A scaffolded app is run and driven in a real browser.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of #1009 that a build cannot make: that the app a template produces
///         actually <b>works</b>. Building proves the code compiles; it says nothing about whether the
///         host serves a page or whether the runtime on it starts.
///     </para>
///     <para>
///         Behind the front-end switch, because a journey builds and RUNS the app, which would double an
///         already expensive gate.
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

    /// <summary>
    ///     A scaffolded Rask app renders its own components and boots its runtime.
    /// </summary>
    /// <remarks>
    ///     A host whose battery wiring is wrong shows here and nowhere else, because building proves
    ///     neither that <c>MapRask&lt;App&gt;()</c> serves a page nor that the runtime on it starts. The
    ///     starter draws no interactive control, so the assertion is the page itself: a Rask component
    ///     rendered by the host, with the runtime script on it and no console error.
    /// </remarks>
    [Theory]
    [InlineData("server")]
    public async Task A_C_sharp_host_renders_its_own_components(string key)
    {
        Assert.SkipUnless(Enabled, SkipReason);

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

        // The browser's own console line for a failed load ("Failed to load resource: … 404") names no URL, so a red
        // run said something was missing and not what. Every failing response is recorded with its address.
        var failedResponses = new List<string>();
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                failedResponses.Add($"{response.Status} {response.Url}");
            }
        };

        await page.GotoAsync(app.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // The starter's own heading, written by a Rask component the HOST rendered.
        await Assertions.Expect(page.GetByText("Hello, Rask!")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // Required empty. This used to allow two console errors (#1078): the page shell emitted a
        // module-script tag for a /main.js the template never builds. That tag left with the render modes
        // (9b4846e0), so a first run has nothing left to excuse, and an allowance kept after its cause is
        // gone would only hide the next error that happened to share its wording.
        Assert.True(
            failures.Count == 0,
            $"--template {key} reached the browser with console errors:\n  "
                + $"{string.Join("\n  ", failures)}\n\nfailed responses:\n  {string.Join("\n  ", failedResponses)}"
                + $"\n\nhost log:\n{app.Log}");
    }

    /// <summary>
    ///     Scaffolds the template, builds it, and starts the host.
    /// </summary>
    private static async Task<ScaffoldedHost> BuildAndRunAsync(string key, TimeSpan readyTimeout)
    {
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "Jny" + key.Replace("-", "", StringComparison.Ordinal);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var projectDirectory = Path.Combine(work, name);

        // Without its WebAssembly client. These journeys run the host as `dotnet run` does and never publish a client,
        // so a server scaffold WITH one answered every request 503 from MapRaskSpa's missing-bundle page, and the
        // server journey could not reach the component it asserts on (#1105).
        var result = TemplateBuildE2ETests.Scaffold(key, projectDirectory, name, version, islands: []);
        TemplateBuildE2ETests.Write(result, projectDirectory, feed);

        var projectFile = Path.Combine(projectDirectory, name + ".csproj");
        var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{projectFile}\" -m:1");

        Assert.True(
            exit == 0,
            $"--template {key} does not build with its front end:\n{CliBuildE2E.Diagnostics(output)}");

        return await ScaffoldedHost.StartAsync(projectDirectory, projectFile, readyTimeout);
    }
}
