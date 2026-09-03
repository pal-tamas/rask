namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     How a TypeScript front end receives Rask's browser layer.
/// </summary>
/// <remarks>
///     <para>
///         The modules live in <c>Rask.Core/Resources/browser/</c> — one source of truth, bundled into
///         Rask's own Server and WASM clients and packed from there into this package as
///         <c>client/browser/</c>, which the build copies into the client's <c>src/rask/browser/</c>
///         beside the generated contracts. No <c>ProjectReference</c> is involved: this package still
///         depends on nothing else in Rask.
///     </para>
///     <para>
///         Asserted on the pack item and the copy step rather than by packing, following the reasoning
///         in <c>PackageDependencyTests</c>: the silently-packs-nothing failure mode needs a glob that
///         runs before its files exist, and these are committed source, always on disk at evaluation.
///         The built <c>.nupkg</c> was checked by hand and does contain <c>client/browser/*.ts</c>
///         with <c>globals.ts</c> absent.
///     </para>
/// </remarks>
public class BrowserLayerDeliveryTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string BrowserDirectory =>
        Path.Combine(_repoRoot, "src", "Rask.Core", "Resources", "browser");

    [Fact]
    public void The_package_ships_the_browser_modules_but_not_the_globals_entry()
    {
        var csproj = File.ReadAllText(
            Path.Combine(_repoRoot, "src", "Rask.Spa.Hosting", "Rask.Spa.Hosting.csproj"));

        Assert.Contains(@"..\Rask.Core\Resources\browser\*.ts", csproj, StringComparison.Ordinal);
        Assert.Contains(@"PackagePath=""client\browser\""", csproj, StringComparison.Ordinal);

        // globals.ts publishes the window.__rask* namespaces .NET resolves dotted identifiers
        // against. Shipping it to a front end would hand a React developer a file whose only purpose
        // is a calling convention they are not using.
        Assert.Contains(@"Exclude=""..\Rask.Core\Resources\browser\globals.ts""", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_copies_them_into_the_clients_generated_directory()
    {
        var targets = File.ReadAllText(Path.Combine(
            _repoRoot, "src", "Rask.Spa.Hosting", "build", "Rask.Spa.Hosting.targets"));

        Assert.Contains("../client/browser/*.ts", targets, StringComparison.Ordinal);
        Assert.Contains(@"DestinationFolder=""$(_RaskSpaGenerated)/browser""", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void No_module_touches_a_DOM_global_at_import_time()
    {
        // The one structural rule of the browser layer, and the one that cannot be caught by a type
        // check: a top-level `window.x = …` runs on IMPORT. In Rask's own bundle that is harmless; in
        // a meta framework's SERVER render it is a ReferenceError before the page renders, and in a
        // bundler it defeats tree-shaking for every consumer of the module.
        //
        // globals.ts is the deliberate exception — being that side effect is its whole job.
        var offenders = Directory.EnumerateFiles(BrowserDirectory, "*.ts")
            .Where(f => Path.GetFileName(f) != "globals.ts")
            .Where(f => File.ReadLines(f).Any(line =>
                line.StartsWith("window.", StringComparison.Ordinal)
                || line.StartsWith("document.", StringComparison.Ordinal)
                || line.StartsWith("navigator.", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These modules touch a DOM global at import time, which breaks a server render and "
            + "defeats tree-shaking — move the side effect into globals.ts: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void There_is_something_to_ship()
    {
        // Guards the guard: every assertion above is vacuous if the directory moved.
        Assert.True(
            Directory.EnumerateFiles(BrowserDirectory, "*.ts").Count() > 1,
            $"No browser modules found under '{BrowserDirectory}' — these tests are checking nothing.");
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
