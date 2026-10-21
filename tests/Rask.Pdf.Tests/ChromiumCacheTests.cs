using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Rask.Printing.Tests;

public sealed class ChromiumCacheTests
{
    [Fact]
    public void The_assembly_carries_the_version_the_pin_file_names()
    {
        var pin = XDocument.Load(PinFile());

        var pinned = pin.Descendants("RaskPdfChromeVersion").Single().Value;

        Assert.Equal(pinned, ChromiumCache.Version);
    }

    [Fact]
    public void Every_platform_the_build_fetches_for_has_a_pinned_checksum()
    {
        var pin = XDocument.Load(PinFile());

        var checksums = pin.Descendants().Where(element => element.Name.LocalName.StartsWith("RaskPdfChromeSha256_", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, checksums.Count);
        Assert.All(checksums, checksum => Assert.Matches("^[0-9a-f]{64}$", checksum.Value));
    }

    [Fact]
    public void Building_this_project_fetched_the_pinned_shell_and_the_locator_finds_it_first()
    {
        var shell = ChromiumCache.PinnedShell();

        var found = ChromiumLocator.Find(configured: null);

        Assert.NotNull(shell);
        Assert.True(File.Exists(shell), $"The build did not fetch the pinned shell to {shell}.");
        Assert.Equal(shell, found);
    }

    private static string PinFile([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Rask.Pdf", "build", "Rask.Pdf.ChromePin.props");
}
