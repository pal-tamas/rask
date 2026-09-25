using System.ComponentModel;

namespace Rask.Storage;

/// <summary>
///     A <c>Share</c> still being worded: <c>await Files.Share(id).For(15.Minutes)</c>. The link is not made
///     until it is awaited, and <c>For</c> is required — a share with no end is <c>Public()</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Sharing
{
    private readonly IFiles? _files;
    private readonly Guid _id;
    private readonly CancellationToken _cancellationToken;

    internal Sharing(IFiles? files, Guid id, CancellationToken cancellationToken)
    {
        _files = files;
        _id = id;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     The link stops working after <paramref name="lifetime" /> — at most 7 days, the same ceiling on
    ///     every store, so a link's life never depends on where the bytes are.
    /// </summary>
    public Task<string?> For(TimeSpan lifetime) =>
        (_files ?? Files.Resolve()).SharedUrl(_id, lifetime, Ambient.Or(_cancellationToken));
}
