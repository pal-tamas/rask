using System.Collections.Immutable;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Reads the template trees embedded in this assembly.
/// </summary>
/// <remarks>
///     <para>
///         The trees live in <c>src/Rask.Templates/</c> and are embedded by <c>Rask.Cli.csproj</c> under
///         the <c>RaskTemplate/</c> prefix, with the resource name carrying the relative path verbatim.
///         That is the whole point of the design: what is committed in that directory is what a scaffold
///         writes, so there is one copy of every template file and no generator to keep in step with it.
///     </para>
///     <para>
///         Resources are read on demand and not cached. A scaffold reads each tree once per process, and
///         holding 2.5 MB of templates alive for the lifetime of a CLI invocation that scaffolds one of
///         them buys nothing.
///     </para>
/// </remarks>
internal static class TemplateAssets
{
    private const string Prefix = "RaskTemplate/";

    /// <summary>Every template key that has a committed tree.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        .. typeof(TemplateAssets).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => Normalize(n[Prefix.Length..]))
            .Select(p => p.Split('/', 2)[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal),
    ];

    /// <summary>Whether <paramref name="templateKey"/> has a committed tree.</summary>
    /// <remarks>
    ///     Matches on the resource PREFIX rather than against <see cref="Keys"/>, which holds only the
    ///     first path segment: the island fragments are addressed as <c>_islands/&lt;runtime&gt;</c>, and
    ///     comparing that to a list of top-level names would say no to every one of them.
    /// </remarks>
    public static bool Has(string templateKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateKey);

        var root = Prefix + templateKey + "/";
        return typeof(TemplateAssets).Assembly.GetManifestResourceNames()
            .Any(n => Normalize(n).StartsWith(root, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Every file in <paramref name="templateKey"/>'s tree, with paths relative to the template root.
    /// </summary>
    public static IReadOnlyList<TemplateAsset> Load(string templateKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateKey);

        var assembly = typeof(TemplateAssets).Assembly;
        var root = Prefix + templateKey + "/";
        var assets = new List<TemplateAsset>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            var normalized = Normalize(name);
            if (!normalized.StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded template resource '{name}' could not be opened.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            assets.Add(new TemplateAsset(normalized[root.Length..], buffer.ToArray()));
        }

        if (assets.Count == 0)
        {
            throw new InvalidOperationException(
                $"No embedded template tree for '{templateKey}'. Templates are embedded from "
                + "src/Rask.Templates/ by Rask.Cli.csproj; a key with no tree means the directory is "
                + "missing or the EmbeddedResource glob no longer reaches it.");
        }

        return [.. assets.OrderBy(a => a.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    ///     MSBuild's <c>%(RecursiveDir)</c> carries the platform separator, so a tree embedded on Windows
    ///     names its resources with backslashes. The path is normalised here rather than at every use.
    /// </summary>
    private static string Normalize(string resourceName) => resourceName.Replace('\\', '/');
}
