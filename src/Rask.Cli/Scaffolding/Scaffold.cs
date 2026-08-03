namespace Rask.Cli.Scaffolding;

/// <summary>Shared path helpers for the generators.</summary>
internal static class Scaffold
{
    /// <summary>
    /// Resolve where a generator writes: an explicit <paramref name="outputOverride"/> (relative to the
    /// working directory) wins; otherwise the generator's <paramref name="defaultSegments"/> under the
    /// working directory. Kept in one place so every generator resolves <c>--output</c> identically.
    /// </summary>
    public static string TargetDirectory(string baseDirectory, string? outputOverride, params string[] defaultSegments)
    {
        if (outputOverride is not null)
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, outputOverride));
        }

        var parts = new string[defaultSegments.Length + 1];
        parts[0] = baseDirectory;
        defaultSegments.CopyTo(parts, 1);
        return Path.Combine(parts);
    }

    /// <summary>
    /// The default folder segments for a cross-cutting artifact (component/job/email): its own feature slice
    /// <c>Features/&lt;Feature&gt;</c> when <c>--feature</c> names one, otherwise the shared bucket
    /// <c>Features/Shared</c>. Both live under the single <c>Features/</c> tree; keeping the "which folder"
    /// decision in one place lets every generator resolve it identically.
    /// </summary>
    public static string[] FeatureOrShared(string? feature) =>
        feature is null ? ["Features", "Shared"] : ["Features", feature];
}
