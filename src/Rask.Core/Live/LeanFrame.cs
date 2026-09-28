using System.Buffers;

namespace Rask.Core.Live;

/// <summary>
///     Slimmed-down <see cref="RenderFrame" /> for the RETAINED clean-subtree cache (Phase B). Drops
///     the three transient fields a held snapshot never needs — <c>ComponentRef</c> (diff-only) and
///     <c>HtmlStart</c>/<c>HtmlEnd</c> (offsets into one render's HTML, regenerated on replay) — so a
///     mounted page retains ~24 bytes per node instead of the full frame's ~40. The live
///     <see cref="RenderFrame" /> stream that <see cref="FrameDiffer" /> walks is unchanged; only the
///     per-component clean-subtree snapshot uses this leaner shape. On replay,
///     <see cref="HtmlSerializer" /> re-emits the HTML AND writes full frames (with fresh offsets) back
///     into the active <see cref="FrameWriter" /> in one pass.
/// </summary>
public struct LeanFrame
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public int SubtreeLength { get; set; }
    public RenderFrameKind Kind { get; set; }
    public bool SelfClosing { get; set; }

    // Retained with the snapshot rather than recomputed: replay writes frames straight back into the
    // live FrameWriter, so a dropped flag would silently un-protect a cached island's subtree and let
    // the next diff patch into React's DOM.
    public bool Opaque { get; set; }
}
