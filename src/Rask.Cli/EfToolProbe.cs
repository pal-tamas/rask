using System.Globalization;

namespace Rask.Cli;

/// <summary>
/// Detects (and, when missing or behind, installs) the Entity Framework Core command-line tools that
/// <c>rask db</c> shells out to. Presence is checked with <c>dotnet ef --version</c> — a zero exit code
/// means the tool is on the path, which covers both a global install and a local tool manifest.
/// </summary>
internal static class EfToolProbe
{
    /// <summary>
    ///     The lowest <c>dotnet-ef</c> that will not complain at a Rask app.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Mirrors the <c>Microsoft.EntityFrameworkCore</c> version in
    ///         <c>Directory.Packages.props</c>, which is the version a scaffolded app actually restores
    ///         through <c>Rask.Data</c>. <c>EfToolProbeTests</c> reads that file and fails if the two ever
    ///         disagree — the same arrangement <see cref="NodeRequirement" /> uses, and for the same
    ///         reason: one number, stated twice, drifts silently otherwise.
    ///     </para>
    ///     <para>
    ///         Below it, every <c>rask db</c> command prints "The Entity Framework tools version 'x' is
    ///         older than that of the runtime 'y'. Update the tools…" before doing the thing it was asked
    ///         to do. That is a chore with a known fix, on a tool this CLI installed in the first place,
    ///         so it is carried out rather than recited.
    ///     </para>
    /// </remarks>
    public static readonly Version Floor = new(10, 0, 12);

    public static async Task<bool> IsInstalledAsync(IProcessRunner process, CancellationToken cancellationToken)
        => await InstalledVersionAsync(process, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>
    ///     The installed tool's version, or null when it is not installed at all.
    /// </summary>
    /// <remarks>
    ///     <c>dotnet ef --version</c> prints a banner and then the version on its own line, so the last
    ///     line that parses as one is the answer. A zero exit with nothing parseable is treated as
    ///     installed-but-unknown (<see cref="UnknownVersion" />) rather than as missing: reinstalling a
    ///     tool that is demonstrably there, because its output changed shape, would be worse than leaving
    ///     it be.
    /// </remarks>
    public static async Task<Version?> InstalledVersionAsync(
        IProcessRunner process,
        CancellationToken cancellationToken)
    {
        var result = await process.CaptureAsync("dotnet", ["ef", "--version"], null, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        foreach (var line in result.StandardOutput.Split('\n').Reverse())
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // "10.0.5", and also "11.0.0-rc.1.12345.6" — the build suffix is not part of the comparison.
            var number = trimmed.Split('-', 2)[0];
            if (Version.TryParse(number, out var version))
            {
                return version;
            }
        }

        return UnknownVersion;
    }

    /// <summary>Installed, but its version could not be read — never treated as behind.</summary>
    public static readonly Version UnknownVersion = new(0, 0, 0);

    /// <summary>
    /// Guarantee the EF Core tools are usable AND current, installing or updating them globally with a
    /// printed notice. Returns <c>true</c> when the tool is available afterwards; on a failed install it
    /// prints the manual command and returns <c>false</c>. This is a dev-time tool, so a
    /// silent-with-notice install is the right DX — no interactive confirmation.
    /// </summary>
    public static async Task<bool> EnsureAsync(IProcessRunner process, IConsole console, CancellationToken cancellationToken)
    {
        var installed = await InstalledVersionAsync(process, cancellationToken).ConfigureAwait(false);

        if (installed is null)
        {
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

        if (installed == UnknownVersion || installed >= Floor)
        {
            return true;
        }

        console.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"The EF Core tools are {installed}, older than the {Floor} runtime this app uses. Updating them…"));

        // Pinned to the floor's MAJOR rather than left floating to `latest`. On a machine with a newer SDK
        // than the app targets, `latest` installs the next major's tools, which then complain in the other
        // direction — and the point of this step is to stop `rask db` printing a version notice at all.
        var updated = await process.RunAsync(
            "dotnet",
            ["tool", "update", "--global", "dotnet-ef", "--version", $"{Floor.Major}.*"],
            null,
            cancellationToken).ConfigureAwait(false);

        if (updated == 0)
        {
            console.Out.WriteLine("Updated dotnet-ef.");
            return true;
        }

        // Not a failure: the tool that is already there still works, it just prints a notice first. Saying
        // so and carrying on beats refusing to run a migration over a version number.
        console.Error.WriteLine("Couldn't update the EF Core tools automatically. Carrying on with the version installed:");
        console.Error.WriteLine($"  dotnet tool update --global dotnet-ef --version {Floor.Major}.*");
        return true;
    }
}
