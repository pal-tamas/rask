namespace Rask.Cli;

/// <summary>
/// Detects (and, when missing, installs) the Entity Framework Core command-line tools that
/// <c>rask db</c> shells out to. Presence is checked with <c>dotnet ef --version</c> — a zero exit code
/// means the tool is on the path, which covers both a global install and a local tool manifest.
/// </summary>
internal static class EfToolProbe
{
    public static async Task<bool> IsInstalledAsync(IProcessRunner process, CancellationToken cancellationToken)
    {
        var result = await process.CaptureAsync("dotnet", ["ef", "--version"], null, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Guarantee the EF Core tools are usable, installing them globally with a printed notice if they
    /// aren't. Returns <c>true</c> when the tool is available afterwards; on a failed install it prints the
    /// manual command and returns <c>false</c>. This is a dev-time tool, so a silent-with-notice install is
    /// the right DX — no interactive confirmation.
    /// </summary>
    public static async Task<bool> EnsureAsync(IProcessRunner process, IConsole console, CancellationToken cancellationToken)
    {
        if (await IsInstalledAsync(process, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        console.Out.WriteLine("The EF Core tools (dotnet-ef) aren't installed. Installing them globally…");
        var exit = await process.RunAsync("dotnet", ["tool", "install", "--global", "dotnet-ef"], null, cancellationToken).ConfigureAwait(false);
        if (exit == 0)
        {
            console.Out.WriteLine("Installed dotnet-ef.");
            return true;
        }

        console.Error.WriteLine("Couldn't install the EF Core tools automatically. Install them and re-run:");
        console.Error.WriteLine("  dotnet tool install --global dotnet-ef");
        return false;
    }
}
