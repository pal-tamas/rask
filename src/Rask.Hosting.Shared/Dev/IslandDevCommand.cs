namespace Rask.Hosting.Shared;

/// <summary>
///     The <c>npx</c> command line that serves a project's islands from a dev server over the config an
///     <see cref="IslandDevPointer" /> names.
/// </summary>
/// <remarks>
///     <para>
///         <c>vp dev</c> when the project installs Vite+, and <c>vite</c> otherwise. A Vite+ project aliases
///         <c>vite</c> to the core Vite+ ships, which carries no <c>vite</c> command, and npx refuses to run
///         <c>vite</c> from a package that names itself something else — so <c>npx vite</c> there fails, and
///         <c>vp</c> is what serves the same config. An older project with Vite alone keeps working as it did.
///     </para>
///     <para>
///         Decided when the server starts, after the build that installed the packages, from the binary npm
///         linked rather than from <c>package.json</c>: what npx can run is what <c>node_modules/.bin</c> holds.
///     </para>
/// </remarks>
internal static class IslandDevCommand
{
    /// <summary>The program both readers start.</summary>
    internal const string FileName = "npx";

    /// <summary>The arguments that serve <paramref name="config" /> for the project at <paramref name="projectDirectory" />.</summary>
    internal static IReadOnlyList<string> Arguments(string projectDirectory, string config)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        ArgumentException.ThrowIfNullOrEmpty(config);

        return UsesVitePlus(projectDirectory)
            ? ["--no-install", "vp", "dev", "--config", config]
            : ["--no-install", "vite", "--config", config];
    }

    /// <summary>Whether npm linked the Vite+ command into the project at <paramref name="projectDirectory" />.</summary>
    internal static bool UsesVitePlus(string projectDirectory) =>
        File.Exists(Path.Combine(projectDirectory, "node_modules", ".bin", "vp"));
}
