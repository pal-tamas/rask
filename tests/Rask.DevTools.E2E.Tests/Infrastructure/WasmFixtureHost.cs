using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.DevTools.E2E.Tests.Infrastructure;

/// <summary>
///     The WASM fixture's Debug publish, with the kit, served from a static host on <c>localhost</c> — a page served from
///     this machine being the other half of what switches the devtools on in a browser.
/// </summary>
/// <remarks>
///     Published by scripts/run-devtools-e2e-local.sh before this assembly is built; the fixture never publishes it
///     itself. The bundle is checked for the devtools before anything is served: a Release publish, or one without the
///     kit, would load a page with no pill and fail every journey on a timeout that says nothing about why.
/// </remarks>
public sealed class WasmFixtureHost : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "tests/Rask.DevTools.Fixture.Wasm";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"The WASM fixture is not published at '{wwwroot}'. Run scripts/run-devtools-e2e-local.sh, which publishes it "
        + "in Debug with the kit (-p:RaskDevToolsFixtureUi=true).";

    protected override void OnBundleLocated(string wwwroot)
    {
        var framework = Path.Combine(wwwroot, "_framework");
        foreach (var assembly in (string[])["Rask.DevTools", "Rask.Ui"])
        {
            if (!Directory.EnumerateFiles(framework, assembly + "*.wasm").Any())
            {
                throw new InvalidOperationException(
                    $"The WASM fixture at '{wwwroot}' carries no {assembly}: the devtools are off in that bundle. "
                    + "Publish it in Debug with -p:RaskDevToolsFixtureUi=true, as scripts/run-devtools-e2e-local.sh does.");
            }
        }
    }
}
