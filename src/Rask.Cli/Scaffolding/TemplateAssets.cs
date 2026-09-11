using System.Collections.Immutable;
using System.Reflection;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     One file in a committed template tree: where it goes, and the bytes that go there.
/// </summary>
/// <param name="Path">The path relative to the template root, always with <c>/</c> separators.</param>
/// <param name="Bytes">The file verbatim. Decoded as text only when the file is text.</param>
internal sealed record TemplateAsset(string Path, byte[] Bytes)
{
    /// <summary>
    ///     Extensions never decoded as text. Reading one of these into a string and writing it back
    ///     re-encodes it as UTF-8 and silently corrupts it — create-vite ships a PNG and two .ico files,
    ///     and a corrupted favicon is the kind of damage that shows up only in a browser.
    /// </summary>
    private static readonly ImmutableHashSet<string> BinaryExtensions =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".png", ".ico", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".woff", ".woff2", ".db");

    /// <summary>Whether this file must be copied byte for byte rather than treated as text.</summary>
    public bool IsBinary => BinaryExtensions.Contains(System.IO.Path.GetExtension(Path));
}

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
    public static bool Has(string templateKey) => Keys.Contains(templateKey, StringComparer.Ordinal);

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
