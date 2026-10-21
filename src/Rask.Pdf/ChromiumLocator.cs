namespace Rask.Printing;

/// <summary>Finds the Chromium the app prints with. It never downloads one: a missing browser is an error that says how to install it.</summary>
internal static class ChromiumLocator
{
    private static readonly string[] CommandNames =
        ["chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "chrome"];

    /// <summary>The configured binary, or the first installed one found in the usual places and on <c>PATH</c>.</summary>
    public static string Find(string? configured) =>
        Find(configured, InstallLocations(), Environment.GetEnvironmentVariable("PATH"));

    internal static string Find(string? configured, IReadOnlyList<string> installLocations, string? path)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured)
                ? configured
                : throw new InvalidOperationException(
                    $"Rask:Pdf:ChromiumPath is '{configured}', and there is no file there. " + HowToInstall);
        }

        var searched = installLocations.Concat(OnPath(path)).ToList();
        return searched.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException(
                "Rask.Pdf prints with Chromium, and none is installed where it looked. " + HowToInstall
                + " Looked in: " + string.Join(", ", searched) + ".");
    }

    private const string HowToInstall =
        "Install Chromium or Google Chrome — `apt-get install -y chromium` in a Debian or Ubuntu image, "
        + "`brew install --cask google-chrome` on a Mac — or point Rask:Pdf:ChromiumPath (environment variable "
        + "Rask__Pdf__ChromiumPath) at the binary. Rask never downloads a browser.";

    private static IEnumerable<string> OnPath(string? path) =>
        (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(directory => CommandNames.Select(name => Path.Combine(directory, name)));

    private static string[] InstallLocations()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return
            [
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
                "/Applications/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing",
                Path.Combine(home, "Applications/Google Chrome.app/Contents/MacOS/Google Chrome"),
                Path.Combine(home, "Applications/Chromium.app/Contents/MacOS/Chromium"),
            ];
        }

        if (OperatingSystem.IsWindows())
        {
            return
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
            ];
        }

        return ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable"];
    }
}
