namespace Rask.Storage;

/// <summary>
///     A stored file an aggregate holds as a value — <c>public Attachment? Document { get; private set; }</c> — saved
///     with the row that holds it, and removed with it.
/// </summary>
/// <remarks>
///     <para>
///         <b>The row and the file are one write.</b> An operation takes the file as <see cref="Of(Saving)" /> —
///         <c>vehicle.Attach(Attachment.Of(Files.Save(upload.OpenReadStream, upload.Name, upload.Size)))</c> — and nothing is
///         stored yet: the bytes are written when the aggregate is saved, inside the operation's transaction. A save that
///         fails or rolls back leaves no file behind; a file the store refuses (too large, a type not allowed) is a
///         <c>RaskValidationException</c> on the property, as any other rule is.
///     </para>
///     <para>
///         <b>A file replaced or cleared is deleted once the save commits</b>, and so is the file of a row deleted for
///         good (a soft-deleted row keeps it). Never before: a save that fails still has its file.
///     </para>
///     <para>
///         It is a value object of four columns — <c>Document_Id</c>, <c>Document_Name</c>, <c>Document_ContentType</c>,
///         <c>Document_Size</c> — so the row says what the file is without a join. Serve it through
///         <c>Files.Download(document.Id)</c> or <c>Files.Share(document.Id)</c>, behind the page's own authorization.
///         It is held on the aggregate or the entity itself; one inside another value object or a list is stored as it
///         is, without the save taking part.
///     </para>
/// </remarks>
public sealed record Attachment
{
    // The upload still to store; null once it is stored, and for every attachment read from a row.
    private readonly Saving? _pending;

    private Attachment(Guid id, string name, string contentType, long size)
    {
        Id = id;
        Name = name;
        ContentType = contentType;
        Size = size;
    }

    private Attachment(Saving pending, string name)
        : this(Guid.Empty, name, "", 0) =>
        _pending = pending;

    /// <summary>The stored file's id: what <c>Files.Download</c>, <c>Files.Share</c> and <c>Files.OpenRead</c> take.</summary>
    public Guid Id { get; private init; }

    /// <summary>The file's display name, reduced to a safe leaf.</summary>
    public string Name { get; private init; }

    /// <summary>The media type sniffed from its bytes (or stated with <c>.As(…)</c>), never the browser's claim.</summary>
    public string ContentType { get; private init; }

    /// <summary>Its size in bytes.</summary>
    public long Size { get; private init; }

    /// <summary>Whether it is still to be stored — by the save of the aggregate that holds it.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>
    ///     A file to store with the row: <c>Attachment.Of(Files.Save(upload.OpenReadStream, upload.Name, upload.Size))</c>, or
    ///     <c>Attachment.Of(Files.Save(xml, "bevallas.xml").As("application/xml"))</c>. Nothing is written until the
    ///     aggregate that holds it is saved.
    /// </summary>
    /// <param name="saving">The save, still being worded — <c>.Public()</c> and <c>.As(…)</c> are kept.</param>
    /// <returns>The attachment, pending until that save.</returns>
    public static Attachment Of(Saving saving) => new(saving, saving.Name);

    /// <summary>A file already stored — the <see cref="StoredFile" /> a <c>Files.Save</c> returned.</summary>
    /// <param name="file">The stored file.</param>
    /// <returns>The attachment naming it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file" /> is null.</exception>
    public static Attachment Of(StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new(file.Id, file.Name, file.ContentType, file.Size);
    }

    /// <summary>Stores the pending file, once, and hands back the attachment naming it.</summary>
    internal async Task<(Attachment Stored, StoredFile File)> Store(CancellationToken cancellationToken)
    {
        var file = await _pending!.Value.In(cancellationToken).AsTask().ConfigureAwait(false);
        return (Of(file), file);
    }
}
