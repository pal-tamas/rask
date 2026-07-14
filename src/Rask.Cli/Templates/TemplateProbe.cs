namespace Rask.Cli.Templates;

/// <summary>
/// Detects whether the Rask project templates are installed, by asking <c>dotnet new list rask</c> and
/// looking for the well-known <c>rask-server</c> short name. Shared by <c>rask new</c> (install on demand)
/// and <c>rask info</c> (report status) so the heuristic lives in exactly one place.
/// </summary>
internal static class TemplateProbe
{
    public static async Task<bool> AreInstalledAsync(IProcessRunner process, CancellationToken cancellationToken)
    {
        var result = await process.CaptureAsync("dotnet", ["new", "list", "rask"], null, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            && result.StandardOutput.Contains("rask-server", StringComparison.Ordinal);
    }
}
