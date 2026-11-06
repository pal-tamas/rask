using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>The browsers a measurement runs in, from Microsoft.Playwright: the binding the E2E suites pin.</summary>
public static class Browsers
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPlaywright? playwright;

    /// <summary>The one Playwright of this run.</summary>
    public static async Task<IPlaywright> Playwright()
    {
        await Gate.WaitAsync();
        try
        {
            return playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Chromium, with text at the font's own advances on every platform. Chromium on Linux hints a face and rounds each
    ///     glyph's advance to a whole pixel (a Mac does not), so a word is 174px there and 172.13px here: whatever Rask.Ui
    ///     draws from widths measured once — a chart's tick labels (inter-metrics), a docs monospace stand-in — and every
    ///     box written at the size Flux gave it then sat a pixel or two off on the runner alone.
    /// </summary>
    public static async Task<IBrowser> Chromium(BrowserTypeLaunchOptions? options = null)
    {
        options ??= new BrowserTypeLaunchOptions();
        options.Args = [.. options.Args ?? [], "--font-render-hinting=none"];
        try
        {
            return await (await Playwright()).Chromium.LaunchAsync(options);
        }
        catch (PlaywrightException error) when (error.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new FluxExit(1, "flux: Chromium is not installed — run: dotnet run --project scripts/flux/Flux -- install chromium");
        }
    }

    /// <summary>Safari's engine where it is installed (<c>flux install webkit</c>), null where it is not.</summary>
    public static async Task<IBrowserType?> WebKit()
    {
        var webkit = (await Playwright()).Webkit;
        return File.Exists(webkit.ExecutablePath) ? webkit : null;
    }
}
