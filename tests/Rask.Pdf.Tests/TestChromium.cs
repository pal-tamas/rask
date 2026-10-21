namespace Rask.Printing.Tests;

/// <summary>
/// The Chromium these tests print with: the one Rask:Pdf:ChromiumPath names (as its environment variable), the one
/// Rask.Pdf finds by itself, or a Chrome for Testing that Playwright already installed on this machine. None of
/// them, and a test that needs a browser is skipped with that reason.
/// </summary>
internal static class TestChromium
{
    private static readonly string? Found = Find();

    public static string Path
    {
        get
        {
            Assert.SkipWhen(
                Found is null,
                "No Chromium or Google Chrome is installed here (set Rask__Pdf__ChromiumPath to one), so nothing printed.");
            return Found!;
        }
    }

    /// <summary>CI runs the browser unsandboxed: the runner's kernel forbids the user namespaces the sandbox needs.</summary>
    public static bool NoSandbox { get; } =
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A printer on the test browser.</summary>
    public static ChromiumPrinter Printer(int maxConcurrentPrints = 2, TimeSpan? timeout = null) => new(new ChromiumSettings
    {
        ChromiumPath = Path,
        NoSandbox = NoSandbox,
        MaxConcurrentPrints = maxConcurrentPrints,
        Timeout = timeout ?? TimeSpan.FromSeconds(60),
    });

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("Rask__Pdf__ChromiumPath") is { Length: > 0 } configured)
        {
            return configured;
        }

        try
        {
            return ChromiumLocator.Find(configured: null);
        }
        catch (InvalidOperationException)
        {
            return PlaywrightsChromium();
        }
    }

    private static string? PlaywrightsChromium()
    {
        var cache = OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Caches/ms-playwright")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/ms-playwright");
        if (!Directory.Exists(cache))
        {
            return null;
        }

        var binary = OperatingSystem.IsMacOS()
            ? "Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing"
            : "chrome";
        return Directory.EnumerateDirectories(cache, "chromium-*")
            .OrderByDescending(directory => directory, StringComparer.Ordinal)
            .SelectMany(directory => Directory.EnumerateDirectories(directory, "chrome-*"))
            .Select(directory => System.IO.Path.Combine(directory, binary))
            .FirstOrDefault(File.Exists);
    }
}
