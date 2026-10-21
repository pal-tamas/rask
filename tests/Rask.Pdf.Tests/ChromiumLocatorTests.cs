namespace Rask.Printing.Tests;

public sealed class ChromiumLocatorTests
{
    [Fact]
    public void A_missing_browser_says_how_to_install_one_and_where_it_looked()
    {
        string[] nowhere = ["/nowhere/Google Chrome"];

        var missing = Assert.Throws<InvalidOperationException>(() => ChromiumLocator.Find(null, nowhere, "/no-bin"));

        Assert.Contains("fetches the pinned Chrome for Testing", missing.Message, StringComparison.Ordinal);
        Assert.Contains("Rask__Pdf__ChromiumPath", missing.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing downloads a browser at run time", missing.Message, StringComparison.Ordinal);
        Assert.Contains("/nowhere/Google Chrome", missing.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("/no-bin", "chromium"), missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_path_with_no_browser_there_is_named_in_the_error()
    {
        var configured = Path.Combine(Path.GetTempPath(), "rask-no-such-chromium");

        var missing = Assert.Throws<InvalidOperationException>(() => ChromiumLocator.Find(configured, [], path: null));

        Assert.Contains($"Rask:Pdf:ChromiumPath is '{configured}'", missing.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing downloads a browser at run time", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_browser_on_the_path_is_found_when_none_is_installed_in_the_usual_places()
    {
        var bin = Directory.CreateTempSubdirectory("rask-pdf-bin-");
        var chromium = Path.Combine(bin.FullName, "chromium");
        File.WriteAllText(chromium, string.Empty);

        var found = ChromiumLocator.Find(null, ["/nowhere/Google Chrome"], bin.FullName);

        Assert.Equal(chromium, found);
        bin.Delete(recursive: true);
    }

    [Fact]
    public async Task A_printer_with_no_browser_fails_the_print_with_the_fix()
    {
        await using var printer = new ChromiumPrinter(new ChromiumSettings { ChromiumPath = "/nowhere/chromium" });

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(
            () => printer.Print(PrintDocument.Of("<p>x</p>"), TestContext.Current.CancellationToken));

        Assert.Contains("Rask:Pdf:ChromiumPath is '/nowhere/chromium'", missing.Message, StringComparison.Ordinal);
    }
}
