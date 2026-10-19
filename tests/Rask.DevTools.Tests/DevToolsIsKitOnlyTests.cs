using System.Runtime.CompilerServices;

namespace Rask.DevTools.Tests;

/// <summary>
///     The panel is drawn with Flux UI's pieces from <c>Rask.Ui</c>, and with nothing else of the kit's.
/// </summary>
/// <remarks>
///     What Flux has no piece for — the component tree, a tab's figures — is the panel's own, built from Flux's pieces and
///     layout Tailwind. The kit's components that are not Flux's are on their way out of the kit.
/// </remarks>
public sealed class DevToolsIsKitOnlyTests
{
    [Fact]
    public void The_panel_draws_with_Flux_pieces_only()
    {
        var source = PackageSourcePath();

        var offenders = FluxKitScan.Offenders(source, SnapshotPath());

        Assert.True(
            offenders.Count == 0,
            "The devtools name kit pieces that are not Flux UI's — draw them with Flux's components instead:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void The_scan_reads_the_panel_and_the_flux_catalogue()
    {
        var files = FluxKitScan.SourceFiles(PackageSourcePath());

        var parts = FluxKitScan.FluxParts(SnapshotPath());

        // Vacuous-pass guards: a path that stopped resolving would find no files and no offenders.
        Assert.Contains(files, f => f.EndsWith("DevToolsTreeTab.cs", StringComparison.Ordinal));
        Assert.Contains("TableColumn", parts);
    }

    // Resolved from the compiler rather than copied to the output directory: the files under test are the ones the
    // package compiles, not a build artifact of them.
    private static string PackageSourcePath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Rask.DevTools"));

    // Flux's catalogue as the kit's conformance gate reads it (declared in the csproj's RaskTestReads).
    private static string SnapshotPath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "Rask.Ui.Tests", "Flux", "flux.snapshot.json"));
}
