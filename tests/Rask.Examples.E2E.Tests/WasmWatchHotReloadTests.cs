using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The last hop of the WASM hot-reload channel, and the only one no unit test can reach: an edit to a
///     C# component reaches the running browser app, the page repaints in place, and the indicator fires
///     — without a navigation.
/// </summary>
/// <remarks>
///     <para>
///         Everything upstream is covered cheaply and deterministically elsewhere —
///         <c>StaticWebAssetsManifestFileProviderTests</c> for serving the build bundle,
///         <c>WasmHotReloadBridgeTests</c> for the announcement, <c>HotReloadClientContractTests</c> for
///         the shared indicator, <c>DevCommandTests</c> for the dev-bundle switch. What is left is the
///         part that only exists in a browser: Mono applying a metadata delta to a running WASM runtime.
///     </para>
///     <para>
///         <b>Opt-in</b> via <c>RASK_WASM_WATCH_E2E=1</c>. It runs a real <c>dotnet watch</c> session with
///         a full WASM build before it can assert anything (minutes, not seconds), and it writes a probe
///         source file into the sample tree, so it has no business in the default browser gate.
///     </para>
/// </remarks>
[Collection(WasmWatchCollection.Name)]
public sealed class WasmWatchHotReloadTests
{
    private const string SkipReason =
        "WASM watch gate: set RASK_WASM_WATCH_E2E=1 to run it. Starts a real `dotnet watch` session over "
        + "the WASM host (full client build), drives an edit, and needs a browser. See scripts/run-wasm-watch-e2e.sh.";

    private static bool Enabled => Environment.GetEnvironmentVariable("RASK_WASM_WATCH_E2E") == "1";

    private readonly PlaywrightFixture _pw;

    public WasmWatchHotReloadTests(PlaywrightFixture pw) => _pw = pw;

    [Fact]
    public async Task An_edit_repaints_the_running_wasm_app_without_reloading_it()
    {
        if (!Enabled)
        {
            // Not SkippableFact: this project has no Xunit.SkippableFact reference, and adding one for a
            // single opt-in case is not worth it. The gate is documented in the class remarks.
            return;
        }

        // Constructed here rather than as a collection fixture on purpose: a collection fixture is
        // built even for a skipped test, so gating it any other way would still pay the whole
        // watch-session startup on every ordinary E2E run.
        var app = new WasmWatchAppFixture();
        await app.InitializeAsync();

        var page = await _pw.Browser.NewPageAsync(new BrowserNewPageOptions { BaseURL = app.BaseUrl });
        try
        {
            await page.GotoAsync("/" + WasmWatchAppFixture.ProbeRoute);

            // Boot: the WASM runtime is up and the probe page rendered. Generous — this is a first-load
            // of an untrimmed development bundle.
            await Expect(page.Locator("#probe"))
                .ToHaveTextAsync(WasmWatchAppFixture.OriginalMarker,
                    new LocatorAssertionsToHaveTextOptions { Timeout = 120_000 });

            // The delta applier arms on this script and nothing else, so its absence would make the rest
            // of the test fail later and much more confusingly.
            //
            // Asserted against the SERVED HTML, not the live DOM: the tag arms the applier during boot,
            // and WASM then morphs the whole document to the .NET-rendered tree — which does not contain
            // it. Looking for it in the DOM afterwards finds nothing and means nothing.
            using (var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) })
            {
                http.DefaultRequestHeaders.Add("Accept", "text/html");
                var shell = await http.GetStringAsync("/" + WasmWatchAppFixture.ProbeRoute);
                Assert.True(
                    shell.Contains("aspnetcore-browser-refresh", StringComparison.Ordinal),
                    $"browser-refresh script missing from the shell — the delta applier never arms.\n{app.ServerLog}");
            }

            // The shared indicator module loaded in the WASM dialect too.
            Assert.True(
                await page.EvaluateAsync<bool>("() => typeof window.__raskHotReloadPill === 'function'"),
                "the shared hot-reload indicator is not present in the WASM client.");

            // Baseline for the no-reload assertion. A full page load would reset this counter and add a
            // navigation entry; a hot reload must do neither.
            await page.EvaluateAsync("() => { window.__raskProbeSentinel = 'alive'; }");
            var navigationsBefore = await page.EvaluateAsync<int>(
                "() => performance.getEntriesByType('navigation').length");

            app.EditProbe(WasmWatchAppFixture.EditedMarker);

            // The headline: the edited literal reaches the running app.
            //
            // Polled rather than Expect(...).ToHaveTextAsync so the failure can carry `dotnet watch`'s
            // own output. Every interesting way this breaks is explained there and nowhere else — "No
            // managed code changes to apply", a rude-edit restart, a build error in the edited file —
            // and without it the report is just "expected X, saw Y" after a two-minute wait.
            var applied = await PollAsync(
                async () => await page.Locator("#probe").InnerTextAsync() == WasmWatchAppFixture.EditedMarker,
                TimeSpan.FromMinutes(2));

            Assert.True(applied, $"the edit never reached the running app.\n--- dotnet watch ---\n{app.ServerLog}");

            // …by applying a delta, not by restarting and reloading. Both halves matter: a restart would
            // also show the new text, and would be a much worse developer experience — so a test that
            // only checked the text would pass on the outcome this feature exists to avoid.
            var sentinel = await page.EvaluateAsync<string?>("() => window.__raskProbeSentinel");
            var navigationsAfter = await page.EvaluateAsync<int>(
                "() => performance.getEntriesByType('navigation').length");

            Assert.True(
                sentinel == "alive" && navigationsAfter == navigationsBefore,
                $"the edit reached the app by RELOADING the page, not by applying a delta "
                + $"(sentinel={sentinel ?? "null"}, navigations {navigationsBefore}→{navigationsAfter}).\n"
                + $"--- dotnet watch ---\n{app.ServerLog}");

            // And the developer was told, which is the difference between "nothing changed" and "your
            // save did nothing".
            Assert.True(
                await page.EvaluateAsync<int>("() => window.__raskHotReloadCount || 0") >= 1,
                $"the hot-reload indicator never fired.\n{app.ServerLog}");
        }
        finally
        {
            await page.CloseAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<bool> PollAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return true;
                }
            }
            catch (PlaywrightException)
            {
                // The node can be mid-morph; keep polling rather than failing on a transient miss.
            }

            await Task.Delay(250);
        }

        return false;
    }
}

[CollectionDefinition(Name)]
public sealed class WasmWatchCollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "WasmWatch";
}
