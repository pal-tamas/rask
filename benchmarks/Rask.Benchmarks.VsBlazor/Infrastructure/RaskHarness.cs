using System.Buffers;
using System.Text;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Benchmarks.VsBlazor.Infrastructure;

/// <summary>
///     Drives a single "previous-state → new-state" render cycle through the same code
///     path <c>LiveSession</c> uses on the wire: capture both renders' RenderFrame
///     streams via the framework's <see cref="SessionRenderCache"/>, build the diff
///     payload via <see cref="LivePayload.BuildPayloadUtf8Diff"/>. Reuses buffers across
///     invocations so steady-state allocations match production.
///     <para>
///         Mirrors the workflow in <c>Rask.Benchmarks/PayloadBytesPerUpdate.cs</c> and
///         <c>Rask.Benchmarks/PayloadBytesReport.cs</c>; lifted into a reusable harness
///         here so every paired Rask-vs-Blazor benchmark can hand a tree in and get back
///         the same numbers <c>LiveSession</c> would ship.
///     </para>
/// </summary>
public sealed class RaskHarness : IDisposable
{
    private readonly SessionRenderCache _cache = new();
    private readonly StringBuilder _htmlBuffer = new(64 * 1024);
    private readonly List<EditOp> _ops = new(32);
    private readonly ArrayBufferWriter<byte> _payloadBuffer = new(64 * 1024);

    /// <summary>
    ///     Render <paramref name="tree"/>, populating the cache's "previous" slot. Call
    ///     once at <c>[GlobalSetup]</c> to seed the before-state. <see cref="TryComputeDiff"/>
    ///     will return <c>false</c> on the very next call only if you call it directly on
    ///     this same render with no further frames; in normal use, call this then call
    ///     <see cref="RenderAndBuildDiffPayloadBytes"/> with the new tree.
    /// </summary>
    public void SeedPrevious(Component tree)
    {
        var writer = _cache.PrepareCurrentBuffer();
        _htmlBuffer.Clear();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, _htmlBuffer);
        }

        // Rotate without diffing: cache now has this frame stream as "previous".
        _cache.Snapshot();
    }

    /// <summary>
    ///     Render <paramref name="tree"/>, diff against the previous capture, build the
    ///     UTF-8 diff payload, and return its byte count. After this call, the cache
    ///     holds this render as the new "previous", so the next call diffs against it.
    /// </summary>
    public int RenderAndBuildDiffPayloadBytes(Component tree)
    {
        var writer = _cache.PrepareCurrentBuffer();
        _htmlBuffer.Clear();
        using (FrameSinkScope.Push(writer))
        {
            HtmlSerializer.Serialize(tree, _htmlBuffer);
        }

        _ops.Clear();
        // newHtml lets InsertSubtree ops carry the HTML fragment for structural inserts
        // (otherwise the caller would have to fall back to the full-HTML payload — that
        // gate is enforced in production by LiveSession; here we just pass the HTML).
        _cache.TryComputeDiff(_ops, _htmlBuffer.ToString());

        _payloadBuffer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8Diff(_payloadBuffer, _ops, null, false);
        return _payloadBuffer.WrittenCount;
    }

    /// <summary>
    ///     Variant: returns the full-HTML payload byte count for this tree — the bytes
    ///     Rask would have shipped before the diff codec existed. Used by Scope 2 to
    ///     report the "full vs diff" reduction ratio side-by-side with Blazor's batch.
    /// </summary>
    public int RenderAndBuildFullPayloadBytes(Component tree)
    {
        _payloadBuffer.ResetWrittenCount();
        var html = tree.RenderAsLiveRoot();
        LivePayload.BuildPayloadUtf8WithRoot(_payloadBuffer, html, "session-bench", null, false);
        return _payloadBuffer.WrittenCount;
    }

    /// <summary>
    ///     Render-to-HTML only — no live root, no payload wrap. Equivalent to calling
    ///     <see cref="Component.ToHtml"/> but with a pooled writer (matches the same
    ///     pool path the framework uses internally).
    /// </summary>
    public string RenderHtml(Component tree)
    {
        _htmlBuffer.Clear();
        HtmlSerializer.Serialize(tree, _htmlBuffer);
        return _htmlBuffer.ToString();
    }

    public void Dispose() => _cache.Dispose();
}
