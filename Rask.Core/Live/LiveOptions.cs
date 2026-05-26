namespace Rask.Core.Live;

/// <summary>
///     How <see cref="LiveSession" /> / <see cref="WasmLiveSession" /> picks the wire
///     payload shape on each render.
/// </summary>
public enum LiveDiffMode
{
    /// <summary>Always ship the full rendered HTML — the behaviour Rask had
    /// before the diff codec landed. Default; safe; matches existing tests and the
    /// existing client morph path.</summary>
    DisabledFull = 0,

    /// <summary>Ship a <see cref="LivePayload.BuildPayloadUtf8Diff" /> payload when
    /// it would be smaller than the full HTML AND the diff contains only ops the
    /// client interpreter knows how to apply without HTML fragments (i.e. no
    /// <see cref="EditOpKind.InsertSubtree" /> / <see cref="EditOpKind.RemoveSubtree" />
    /// today). Otherwise fall back to full HTML transparently. Once HtmlSerializer
    /// captures per-frame byte offsets, the InsertSubtree-carries-HTML path opens
    /// up and the heuristic relaxes.</summary>
    Auto = 1,

    /// <summary>Always ship a diff payload when one is computable. Mostly useful
    /// for tests and benchmarks that want to lock in the diff path regardless of
    /// the byte-size heuristic. Production use is discouraged — falls back to
    /// full HTML on the first render and on out-of-band side effects (auth,
    /// download) that the diff wire format doesn't yet carry.</summary>
    Forced = 2
}

/// <summary>
///     Per-app live-runtime options exposed through
///     <c>services.AddRask(o => o.DiffMode = ...)</c>. Defaults are tuned for the
///     "byte-savings out of the box" experience: <see cref="DiffMode" /> is
///     <see cref="LiveDiffMode.Auto" /> so a fresh app sees counter updates and
///     similar in-place state changes ship as a handful of bytes instead of the
///     whole rendered body. Override to <see cref="LiveDiffMode.DisabledFull" />
///     for bit-for-bit pre-codec behaviour, or to <see cref="LiveDiffMode.Forced" />
///     for testing the diff path unconditionally.
/// </summary>
public sealed class RaskLiveOptions
{
    public LiveDiffMode DiffMode { get; set; } = LiveDiffMode.Auto;
}

/// <summary>
///     Static accessor for the active diff-mode setting. Set by
///     <c>AddRask()</c> from the configured <see cref="RaskLiveOptions" />; the
///     live-session runtime (server + WASM) reads from here on every render so
///     the option flow stays trivially fast — no DI lookup in the hot path. Hosts
///     that don't go through <c>AddRask()</c> (some standalone WASM bootstraps)
///     can also write this property directly.
/// </summary>
public static class LiveOptions
{
    public static LiveDiffMode DiffMode { get; set; } = LiveDiffMode.Auto;
}
