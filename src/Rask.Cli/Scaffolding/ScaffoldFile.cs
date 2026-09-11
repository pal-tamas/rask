namespace Rask.Cli.Scaffolding;

/// <summary>A single file a generator wants to write: its absolute <see cref="Path"/> and <see cref="Content"/>.</summary>
/// <remarks>
///     <see cref="Bytes"/> is set instead of <see cref="Content"/> for a file that is not text. The
///     templates carry three — a PNG and two .ico favicons that create-vite and create-next-app ship —
///     and a byte array is the only honest way to carry them: decoding one into a string and writing it
///     back re-encodes it as UTF-8, which corrupts it silently and shows up only in a browser.
/// </remarks>
internal sealed record ScaffoldFile(string Path, string Content)
{
    /// <summary>The file verbatim, for a non-text file. Null means <see cref="Content"/> is the file.</summary>
    public byte[]? Bytes { get; init; }
}
