using Microsoft.Playwright;

namespace Rask.Examples.E2E.Tests.Infrastructure;

internal static class TestArtifacts
{
    private static readonly string Root = Path.Combine(LocateRepoRoot(), "TestResults", "E2E");

    public static async Task DumpAsync(IPage page, string fixtureName, string testName, string serverLog, string[]? console = null)
    {
        var safeName = string.Concat(testName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        var dir = Path.Combine(Root, fixtureName, safeName);
        Directory.CreateDirectory(dir);

        if (console is { Length: > 0 })
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "console.txt"), string.Join('\n', console));
        }

        try
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(dir, "page.png"), FullPage = true
            });
        }
        catch
        {
            /* page may already be closed */
        }

        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "page.html"), await page.ContentAsync());
        }
        catch
        {
            /* page may already be closed */
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "server.log"), serverLog);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
