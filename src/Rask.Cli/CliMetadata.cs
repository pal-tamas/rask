using System.Reflection;

namespace Rask.Cli;

/// <summary>Tool identity — the version MinVer stamps onto the assembly at pack time.</summary>
internal static class CliMetadata
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informational = typeof(CliMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return typeof(CliMetadata).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        // MinVer appends "+<commit-sha>" build metadata; trim it for a clean display version.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}
