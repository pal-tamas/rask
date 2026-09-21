using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.SQLite.Browser.E2E.Tests;

/// <summary>The fixture app's publish, served as a static site the way any host would serve it.</summary>
public sealed class FullTextFixtureHost : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "tests/Rask.SQLite.Browser.Fixture.Wasm";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"The full-text fixture is not published at '{wwwroot}'. Run scripts/run-browser-sqlite-e2e-local.sh, which "
        + "publishes it with native linking.";

    // Without native linking e_sqlite3 is absent and every journey would time out on "loading"; say so up front.
    protected override void OnBundleLocated(string wwwroot)
    {
        var framework = Path.Combine(wwwroot, "_framework");
        if (!Directory.EnumerateFiles(framework, "Microsoft.EntityFrameworkCore.Sqlite*.wasm").Any())
        {
            throw new InvalidOperationException(
                $"The bundle at '{wwwroot}' carries no EF Core SQLite provider; it is not the full-text fixture's publish.");
        }
    }
}
