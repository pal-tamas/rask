using System.Reflection;

namespace Rask.Cli;

/// <summary>
///     The Entity Framework Core version this build of Rask is compiled against.
/// </summary>
/// <remarks>
///     Stamped from the central <c>Microsoft.EntityFrameworkCore</c> pin by the <c>RaskStampEfCoreVersion</c>
///     target in <c>Rask.Cli.csproj</c>, so it follows a Dependabot bump without a second number to edit. Every
///     EF package the CLI puts into an app is pinned to it: a floating reference resolves to the newest EF on
///     nuget.org, which runs ahead of the one <c>Rask.Server</c> restores and leaves the app with two EF versions.
/// </remarks>
internal static class EfCorePin
{
    /// <summary>The pinned version, e.g. <c>10.0.13</c>.</summary>
    public static string Version { get; } = typeof(EfCorePin).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(metadata => string.Equals(metadata.Key, "RaskEfCoreVersion", StringComparison.Ordinal))?.Value
        ?? throw new InvalidOperationException("Rask.Cli was built without its RaskEfCoreVersion stamp.");
}
