using System.Text;

namespace Rask.Core.Live;

/// <summary>
///     Per-session cache of the previous render's <see cref="RenderFrame" /> stream
///     so the diff codec can compare against it on the next render. Owns two
///     <see cref="FrameWriter" /> buffers — one for the "previous" snapshot the
///     client currently has, one for the render in flight — and rotates them.
///     <para>
///         Steady-state allocation is zero across renders: both frame buffers
///         survive in pooled storage; the underlying arrays grow once to the
///         high-water mark and then re-use forever.
///     </para>
///     <para>
///         Two ways to use this:
///         <list type="bullet">
///             <item>
///                 <description><see cref="Render" />: the cache drives the render
///                 directly. Returns the diff (or <c>null</c> on first render).</description>
///             </item>
///             <item>
///                 <description><see cref="PrepareCurrentBuffer" /> +
///                 <see cref="TryComputeDiff" />: caller drives the render however it
///                 likes (typically through <c>RenderAsLiveRoot</c>) while pushing
///                 the returned buffer onto <see cref="FrameSinkScope" /> first. Use
///                 this from <c>LiveSession</c>, which has its own render flow.</description>
///             </item>
///         </list>
///     </para>
/// </summary>
public sealed class SessionRenderCache : IDisposable
{
    private FrameWriter? _previous;
    private FrameWriter? _current;
    private bool _hasPrevious;

    /// <summary>
    ///     Acquire the buffer the caller should push onto
    ///     <see cref="FrameSinkScope" /> before invoking the render path. Resets
    ///     it to empty so the current render's frames write from offset 0.
    /// </summary>
    public FrameWriter PrepareCurrentBuffer()
    {
        _current ??= new FrameWriter();
        _current.Reset();
        return _current;
    }

    /// <summary>
    ///     After the render has populated <see cref="PrepareCurrentBuffer" />,
    ///     compute the diff against the previous render and rotate buffers.
    ///     Returns <c>true</c> when a diff was produced (caller may ship it),
    ///     <c>false</c> on the first render of the session (no prior to diff
    ///     against — caller must ship full HTML). Passing <paramref name="newHtml" />
    ///     lets <see cref="FrameDiffer.Diff" /> attach HTML fragments to
    ///     <see cref="EditOpKind.InsertSubtree" /> ops for the client interpreter.
    /// </summary>
    public bool TryComputeDiff(List<EditOp> output, string? newHtml = null)
    {
        output.Clear();
        if (!_hasPrevious || _current is null)
        {
            RotateBuffers();
            return false;
        }

        FrameDiffer.Diff(_previous!.WrittenSpan, _current.WrittenSpan, output, newHtml);
        RotateBuffers();
        return true;
    }

    /// <summary>
    ///     Promote the current render to "previous" without computing a diff.
    ///     Use this when the caller decided to ship the full HTML for this render
    ///     (e.g. out-of-band side effects, structural ops the diff path can't carry,
    ///     navigation) — the client still receives the new state, so the cache must
    ///     stay in lockstep. Skipping this leaves <c>_previous</c> stale, which
    ///     corrupts the next diff: edits computed against an out-of-date snapshot
    ///     applied to a DOM the client has already moved past.
    /// </summary>
    public void Snapshot()
    {
        RotateBuffers();
    }

    /// <summary>
    ///     One-shot render + diff. Mostly useful for tests and the
    ///     <c>payload-bytes</c> report; production callers use
    ///     <see cref="PrepareCurrentBuffer" /> + <see cref="TryComputeDiff" />
    ///     so they keep control of the render path.
    /// </summary>
    public bool Render(
        Component rootComponent,
        StringBuilder htmlOutput,
        List<EditOp> diffOps)
    {
        var writer = PrepareCurrentBuffer();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(rootComponent, htmlOutput);
        }

        return TryComputeDiff(diffOps);
    }

    private void RotateBuffers()
    {
        (_previous, _current) = (_current, _previous);
        _hasPrevious = true;
    }

    public void Dispose()
    {
        _previous = null;
        _current = null;
        _hasPrevious = false;
    }
}
