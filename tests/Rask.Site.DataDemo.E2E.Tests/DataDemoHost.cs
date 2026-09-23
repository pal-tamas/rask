using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Site.DataDemo.E2E.Tests;

/// <summary>The data demo's publish, served under <c>/demos/data</c> as GitHub Pages serves it beside the site.</summary>
public sealed class DataDemoHost : StaticWwwrootHostFixture
{
    /// <summary>Where the demo lives on rask.sh, and where the published <c>&lt;base href&gt;</c> points.</summary>
    public const string Path = "/demos/data";

    protected override string ProjectRelativePath => "src/Rask.Site.DataDemo";

    protected override string MountPath => Path;

    /// <summary>The demo's own URL.</summary>
    public string DemoUrl => BaseUrl + Path + "/";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"The data demo is not published at '{wwwroot}'. Run scripts/run-data-demo-e2e-local.sh, which publishes it "
        + "with native linking.";

    // Without native linking e_sqlite3 is absent, and a publish without the path base would load nothing under
    // /demos/data; either way every journey would time out on the boot screen, so say which one up front.
    protected override void OnBundleLocated(string wwwroot)
    {
        var framework = System.IO.Path.Combine(wwwroot, "_framework");
        if (!Directory.EnumerateFiles(framework, "Microsoft.EntityFrameworkCore.Sqlite*.wasm").Any())
        {
            throw new InvalidOperationException(
                $"The bundle at '{wwwroot}' carries no EF Core SQLite provider; it is not the data demo's publish.");
        }

        var index = File.ReadAllText(System.IO.Path.Combine(wwwroot, "index.html"));
        if (!index.Contains($"<base href=\"{Path}/\"/>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The data demo at '{wwwroot}' was not published with <base href=\"{Path}/\">, so it cannot load under {Path}.");
        }
    }
}
