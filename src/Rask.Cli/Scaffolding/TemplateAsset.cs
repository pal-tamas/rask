using System.Collections.Immutable;

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
    ///     re-encodes it as UTF-8 and silently corrupts it — a corrupted favicon is the kind of damage
    ///     that shows up only in a browser.
    /// </summary>
    private static readonly ImmutableHashSet<string> BinaryExtensions =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".png", ".ico", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".woff", ".woff2", ".db");

    /// <summary>Whether this file must be copied byte for byte rather than treated as text.</summary>
    public bool IsBinary => BinaryExtensions.Contains(System.IO.Path.GetExtension(Path));
}
