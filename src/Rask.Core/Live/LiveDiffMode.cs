namespace Rask.Core.Live;

/// <summary>
///     How <c>LiveSession</c> / <c>WasmLiveSession</c> picks the wire
///     payload shape on each render.
/// </summary>
public enum LiveDiffMode
{
    /// <summary>
    ///     Always ship the full rendered HTML — the behaviour Rask had
    ///     before the diff codec landed. Default; safe; matches existing tests and the
    ///     existing client morph path.
    /// </summary>
    DisabledFull = 0,

    /// <summary>
    ///     Ship a <see cref="LivePayload.BuildPayloadUtf8Diff" /> payload
    ///     whenever a diff is computable and client-applicable. Falls back to full HTML
    ///     only when the diff would be <em>larger</em> than re-sending the body, on the
    ///     first render (no baseline), on structural ops the client interpreter can't
    ///     apply without morph-quality book-keeping (positional
    ///     <see cref="EditOpKind.InsertSubtree" /> / <see cref="EditOpKind.RemoveSubtree" />
    ///     / <see cref="EditOpKind.MoveSubtree" /> — keyed list edits are diffed), and on
    ///     out-of-band side effects (auth, download, jsInvokes, navigation). This is the
    ///     default: any genuine in-place state change ships as a diff regardless of page
    ///     size.
    /// </summary>
    Auto = 1,

    /// <summary>
    ///     Always ship a diff payload when one is computable,
    ///     <em>
    ///         even when it
    ///         would be larger than the full HTML
    ///     </em>
    ///     . Mostly useful for tests and
    ///     benchmarks that want to lock in the diff path regardless of byte size.
    ///     Otherwise identical to <see cref="Auto" /> — still falls back to full HTML on
    ///     the first render, on structural ops, and on out-of-band side effects (auth,
    ///     download) that the diff wire format doesn't yet carry.
    /// </summary>
    Forced = 2
}
