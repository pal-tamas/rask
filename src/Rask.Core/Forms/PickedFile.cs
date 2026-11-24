namespace Rask.Core.Forms;

/// <summary>
///     A file the reader picked in a form, read into memory as it was picked — so it is still there when the form is
///     submitted, long after the browser's own handle has gone. What a form over an operation that takes an
///     <c>Attachment</c> holds in that field, and what <c>Ui.Field.Bind</c> draws as a <c>Ui.FileUpload</c>.
/// </summary>
/// <remarks>
///     Its name, size and media type are the browser's word: the store sniffs the type from the bytes and checks the size
///     again when the file is saved, and that check is the one that holds.
/// </remarks>
public sealed class PickedFile
{
    /// <summary>The most a picked file is read up to when nothing else is said: the store's own default limit, 50 MB.</summary>
    public const long DefaultMaxSize = 50 * 1024 * 1024;

    private readonly byte[] _content;

    private PickedFile(string name, string contentType, byte[] content)
    {
        Name = name;
        ContentType = contentType;
        _content = content;
    }

    /// <summary>The file's name as the reader's machine called it.</summary>
    public string Name { get; }

    /// <summary>The media type the browser claimed.</summary>
    public string ContentType { get; }

    /// <summary>How many bytes were read.</summary>
    public long Size => _content.LongLength;

    /// <summary>Reads <paramref name="file" /> into memory while its handle is still open.</summary>
    /// <param name="file">The file the browser handed over.</param>
    /// <param name="maxSize">The most bytes to read; a larger file is refused.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The picked file.</returns>
    /// <exception cref="IOException">The file is larger than <paramref name="maxSize" />.</exception>
    public static async Task<PickedFile> Read(IRaskFile file, long maxSize = DefaultMaxSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Size > maxSize)
        {
            throw new IOException($"'{file.Name}' is {file.Size} bytes, more than the {maxSize} a file may be.");
        }

        using var buffer = new MemoryStream();
        var source = file.OpenReadStream(maxSize, cancellationToken);
        await using (source.ConfigureAwait(false))
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        return new PickedFile(file.Name, file.ContentType, buffer.ToArray());
    }

    /// <summary>A file made in code — a test's, or one the page built itself.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="content">Its bytes.</param>
    /// <param name="contentType">Its media type, as a browser would claim it.</param>
    /// <returns>The picked file.</returns>
    public static PickedFile Of(string name, byte[] content, string contentType = "application/octet-stream")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);
        return new PickedFile(name, contentType, [.. content]);
    }

    /// <summary>Opens what was read, from the start.</summary>
    /// <returns>A read-only stream over the bytes.</returns>
    public Stream OpenRead() => new MemoryStream(_content, writable: false);

    /// <summary>The name, as a list of files shows it.</summary>
    /// <returns>The file's name.</returns>
    public override string ToString() => Name;
}
