namespace Rask.Cli;

/// <summary>The platforms whose "open a URL" command differs. Explicit so every branch is testable from any host OS.</summary>
internal enum BrowserPlatform
{
    MacOS,
    Windows,
    Linux
}

/// <summary>
///     The seam through which <c>rask dev --open</c> opens a browser. Faked in tests so the suite never
///     spawns one.
/// </summary>
internal interface IBrowserLauncher
{
    /// <summary>
    ///     Opens <paramref name="url" />. Returns false if the platform command could not be started —
    ///     never throws, and never affects the exit code: <c>rask dev</c> must not die because a browser
    ///     didn't open.
    /// </summary>
    Task<bool> TryOpenAsync(string url, CancellationToken cancellationToken);
}

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

    internal static BrowserPlatform Current() =>
        OperatingSystem.IsMacOS() ? BrowserPlatform.MacOS
        : OperatingSystem.IsWindows() ? BrowserPlatform.Windows
        : BrowserPlatform.Linux;

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
