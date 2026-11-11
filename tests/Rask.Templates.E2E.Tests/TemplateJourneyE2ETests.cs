using Microsoft.Playwright;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Testing;

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
/// </remarks>
public sealed class TemplateJourneyE2ETests(PlaywrightFixture browser) : IClassFixture<PlaywrightFixture>
{
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
        Assert.SkipUnless(TemplateBuildE2ETests.Enabled, TemplateBuildE2ETests.SkipReason);

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
    ///     A scaffolded app passes Rask.Testing.Playwright's walk on every page a visitor opens, and on a page behind its
    ///     sign-in as its administrator — signed in through Rask.Auth's own API, as the app's first account.
    /// </summary>
    /// <remarks>
    ///     The walk an app's own E2E project runs (docs/testing.md, "Testing an app end to end"), over the app <c>rask new</c>
    ///     writes: a console error, a failed request, a not-found or forbidden page, or a page wider than a phone fails it.
    /// </remarks>
    [Fact]
    public async Task A_scaffolded_app_walks_whole_signed_out_and_as_its_administrator()
    {
        Assert.SkipUnless(TemplateBuildE2ETests.Enabled, TemplateBuildE2ETests.SkipReason);

        var (projectDirectory, name) = await ScaffoldAndBuildAsync("server", "Walk");
        try
        {
            // The first migration, as `rask new` makes it: the accounts live in tables only a migration creates.
            var console = new StringConsole();
            var db = new DbCommand(console, new SystemFileSystem(), new ProcessRunner(), projectDirectory,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["RaskSpaBuild"] = "false" });
            var migrated = await db.ExecuteAsync(["add", "Init", "--project", Path.Combine(projectDirectory, name + ".csproj")],
                TestContext.Current.CancellationToken);
            Assert.True(migrated == 0, $"the first migration failed.\n{console.OutText}\n{console.ErrorText}");
            var (rebuilt, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDirectory, name + ".csproj")}\" -m:1");
            Assert.True(rebuilt == 0, $"the app does not build with its migration:\n{CliBuildE2E.Diagnostics(output)}");

            await using var site = new RaskSite(Path.Combine(projectDirectory, "bin", "Debug", "net11.0", name + ".dll"))
            {
                ContentRoot = projectDirectory,
            };

            foreach (var path in (string[])["/", "/login", "/register", "/forgot-password"])
            {
                await site.Walk(path);
            }

            // Behind the sign-in: a walk signed in that is sent to /login fails, so this passing is the sign-in holding.
            await site.Walk("/devices").As(site.Admin);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(Path.GetDirectoryName(projectDirectory)!);
        }
    }

    /// <summary>
    ///     Scaffolds the template, builds it, and starts the host.
    /// </summary>
    private static async Task<ScaffoldedHost> BuildAndRunAsync(string key, TimeSpan readyTimeout)
    {
        var (projectDirectory, name) = await ScaffoldAndBuildAsync(key, "Jny");
        return await ScaffoldedHost.StartAsync(projectDirectory, Path.Combine(projectDirectory, name + ".csproj"), readyTimeout);
    }

    /// <summary>
    ///     Scaffolds the template under a name starting <paramref name="prefix" /> and builds it.
    /// </summary>
    private static async Task<(string ProjectDirectory, string Name)> ScaffoldAndBuildAsync(string key, string prefix)
    {
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = prefix + key.Replace("-", "", StringComparison.Ordinal);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var projectDirectory = Path.Combine(work, name);

        // Without its WebAssembly client. These journeys run the host as `dotnet run` does and never publish a client,
        // so a server scaffold WITH one answered every request 503 from MapRaskSpa's missing-bundle page, and the
        // server journey could not reach the component it asserts on (#1105).
        var result = TemplateBuildE2ETests.Scaffold(key, projectDirectory, name, version);
        TemplateBuildE2ETests.Write(result, projectDirectory, feed);

        var projectFile = Path.Combine(projectDirectory, name + ".csproj");
        var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{projectFile}\" -m:1");

        Assert.True(
            exit == 0,
            $"--template {key} does not build:\n{CliBuildE2E.Diagnostics(output)}");

        return (projectDirectory, name);
    }
}
