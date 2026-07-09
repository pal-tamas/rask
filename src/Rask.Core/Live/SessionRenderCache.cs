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
///                 <description>
///                     <see cref="Render" />: the cache drives the render
///                     directly. Returns the diff (or <c>null</c> on first render).
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <see cref="PrepareCurrentBuffer" /> +
///                     <c>TryComputeDiff</c>: caller drives the render however it
///                     likes (typically through <c>RenderAsLiveRoot</c>) while pushing
///                     the returned buffer onto <see cref="FrameSinkScope" /> first. Use
///                     this from <c>LiveSession</c>, which has its own render flow.
///                 </description>
///             </item>
///         </list>
///     </para>
/// </summary>
public sealed class SessionRenderCache : IDisposable
{
    private FrameWriter? _current;
    private bool _hasPrevious;
    private FrameWriter? _previous;

    // Reusable keyed-diff scratch, owned per session so the keyed reconciliation path
    // (FrameDiffer.DiffKeyedSiblings) is allocation-free in steady state. Lazily created
    // on first diff; nulled on Dispose alongside the frame buffers.
    private FrameDiffer.DiffScratch? _scratch;

    public void Dispose()
    {
        _previous = null;
        _current = null;
        _hasPrevious = false;
        _scratch = null;
    }

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
    ///     lets <c>FrameDiffer.Diff</c> attach HTML fragments to
    ///     <see cref="EditOpKind.InsertSubtree" /> ops for the client interpreter.
    /// </summary>
    public bool TryComputeDiff(List<EditOp> output, ReadOnlySpan<char> newHtml = default)
        => TryComputeDiff(output, out _, newHtml);

    /// <summary>
    ///     Diff variant that can DEFER the buffer rotation. The coalescing render loops
    ///     (<c>WasmLiveSession.BuildPayloadCoalescingRerendersAsync</c>) build a payload
    ///     several times within one dispatch but only the LAST build is sent; rotating on
    ///     each intermediate build would make the final build diff against an un-sent render
    ///     (producing a payload that doesn't reflect the client's actual DOM). Passing
    ///     <paramref name="rotate" /><c>=false</c> diffs against the stable last-sent baseline
    ///     without promoting <c>_current</c>; the caller commits exactly once via
    ///     <see cref="Snapshot" /> after the loop settles.
    /// </summary>
    public bool TryComputeDiff(List<EditOp> output, bool rotate, ReadOnlySpan<char> newHtml = default)
        => TryComputeDiff(output, out _, newHtml, rotate);

    /// <summary>
    ///     Variant that surfaces whether the diff used the keyed-matching path at any depth.
    ///     The live-session gates (<c>LiveSession.DiffOpsAreClientSupported</c>,
    ///     <c>WasmLiveSession.DiffOpsAreClientSupported</c>) use this to decide whether to
    ///     trust structural ops on the wire: keyed-driven Move/Insert/Remove preserve DOM
    ///     identity on surviving nodes (focus, IDL state, listeners), so they're safe to
    ///     ship as diff; positional structural ops still route to the full-HTML morph path.
    /// </summary>
    public bool TryComputeDiff(List<EditOp> output, out bool usedKeyedPath, ReadOnlySpan<char> newHtml = default)
        => TryComputeDiff(output, out usedKeyedPath, newHtml, true);

    public bool TryComputeDiff(List<EditOp> output, out bool usedKeyedPath, ReadOnlySpan<char> newHtml, bool rotate)
    {
        output.Clear();
        usedKeyedPath = false;
        if (!_hasPrevious || _current is null)
        {
            if (rotate)
            {
                RotateBuffers();
            }

            return false;
        }

        FrameDiffer.Diff(_previous!.WrittenSpan, _current.WrittenSpan, output,
            _scratch ??= new FrameDiffer.DiffScratch(), out usedKeyedPath, newHtml);
        if (rotate)
        {
            RotateBuffers();
        }

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
    /// <summary>
    ///     True when the last <c>TryComputeDiff</c> touched a sibling level that mixed a Raw
    ///     frame with other siblings and emitted ops there — the positional paths are unreliable, so
    ///     the session must ship full HTML (the morph reparses the Raw markup) rather than the diff.
    /// </summary>
    public bool LastDiffForcedFullHtml => _scratch?.ForceFullHtml ?? false;

    public void Snapshot() => RotateBuffers();

    /// <summary>
    ///     One-shot render + diff. Mostly useful for tests and the
    ///     <c>payload-bytes</c> report; production callers use
    ///     <see cref="PrepareCurrentBuffer" /> + <c>TryComputeDiff</c>
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
}
