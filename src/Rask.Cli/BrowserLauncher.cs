namespace Rask.Cli;

internal sealed class BrowserLauncher(IProcessRunner process) : IBrowserLauncher
{
    private readonly IProcessRunner _process = process;

    public async Task<bool> TryOpenAsync(string url, CancellationToken cancellationToken)
    {
        var (fileName, arguments) = CommandFor(Current(), url);
        try
        {
            await _process.RunAsync(fileName, arguments, null, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // No browser, no shell, a locked-down container — none of it is worth failing the run over.
            return false;
        }
    }

    internal static BrowserPlatform Current()
    {
        if (OperatingSystem.IsMacOS())
        {
            return BrowserPlatform.MacOS;
        }

        return OperatingSystem.IsWindows() ? BrowserPlatform.Windows : BrowserPlatform.Linux;
    }

    /// <summary>
    ///     The platform's open-a-URL command. Pure, and takes the platform explicitly, so all three
    ///     branches are asserted regardless of which OS the tests run on.
    /// </summary>
    internal static (string FileName, IReadOnlyList<string> Arguments) CommandFor(BrowserPlatform platform, string url) =>
        platform switch
        {
            BrowserPlatform.MacOS => ("open", new[] { url }),
            // `start` is a cmd builtin, not an executable. The empty string is its window-title
            // argument — without it a URL containing '&' is taken as the title and nothing opens.
            BrowserPlatform.Windows => ("cmd", new[] { "/c", "start", "", url }),
            _ => ("xdg-open", new[] { url })
        };
}
