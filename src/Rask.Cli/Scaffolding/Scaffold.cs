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
}
