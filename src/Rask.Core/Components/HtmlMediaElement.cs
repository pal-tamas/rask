using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

// Shared base for the media elements (Audio, Video), mirroring the DOM `HTMLMediaElement`
// interface. Carries the attributes common to every media element so the concrete tags don't
// each redeclare them; tag-specific extras (e.g. Video's poster/width/height/playsinline) stay
// on the subclass and emit after the shared block (base.WriteAttributes runs first).
//
// It also adds the HTMLMediaElement-specific EVENTS (play/pause/timeupdate/…) — these are not part of
// the universal GlobalEventHandlers surface, so they live here rather than on Element. They flow through
// the same LiveState DomEvents store (sync + async `Action<MediaEventArgs>` / `Func<MediaEventArgs, Task>`
// pairs, see ElementEvents) and emit after the shared attrs.

/// <summary>
///     The attributes <c>audio</c> and <c>video</c> share. Not a tag of its own — it exists so both media
///     elements expose one playback surface, and so their factories order those parameters identically.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement">MDN:
///     HTMLMediaElement</see>
/// </summary>
public abstract class HtmlMediaElement : Element
{
    // Emit order for the media events, kept deterministic like Element's GlobalEventOrder.
    private static readonly string[] MediaEventOrder =
    {
        "play", "pause", "playing", "ended", "timeupdate", "volumechange", "ratechange", "durationchange",
        "loadedmetadata", "seeked", "seeking", "waiting"
    };

    /// <summary>
    ///     The URL of the media to play. Prefer <c>source</c> children when you want to offer more than one
    ///     encoding.
    /// </summary>
    public string? Src { get; set; }

    /// <summary>
    ///     Shows the browser's own playback controls. Without it — and without controls of your own — the
    ///     media cannot be played at all.
    /// </summary>
    public bool? Controls { get; set; }

    /// <summary>
    ///     Starts playback as soon as enough has buffered. Browsers block autoplay with sound until the
    ///     user has interacted with the page; pair it with <c>Muted</c> if you need it to fire.
    /// </summary>
    public bool? Autoplay { get; set; }

    /// <summary>Restarts from the beginning each time playback reaches the end.</summary>
    public bool? Loop { get; set; }

    /// <summary>Silences the audio initially.</summary>
    public bool? Muted { get; set; }

    /// <summary>
    ///     How much to fetch before playback: <c>none</c>, <c>metadata</c>, or <c>auto</c>. A hint the
    ///     browser may ignore.
    /// </summary>
    public string? Preload { get; set; }

    /// <summary>
    ///     The CORS mode for the fetch — <c>anonymous</c> or <c>use-credentials</c>. Required before a
    ///     cross-origin video can be drawn to a canvas.
    /// </summary>
    public string? CrossOrigin { get; set; }

    /// <summary>
    ///     Playback was requested — the moment <c>play()</c> was called or the user hit play, which is
    ///     <em>before</em> a single frame has been shown. For the point where it is actually running, use
    ///     <c>playing</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/play_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnPlay { get => SyncHandler<MediaEventArgs>("play"); set => SetDomEventSync("play", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPlay"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnPlayAsync { get => AsyncHandler<MediaEventArgs>("play"); set => SetDomEventAsync("play", value); }

    /// <summary>
    ///     Playback was paused. Fires on a real pause, not at the end of the media — <c>ended</c> covers that.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/pause_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnPause { get => SyncHandler<MediaEventArgs>("pause"); set => SetDomEventSync("pause", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPause"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnPauseAsync { get => AsyncHandler<MediaEventArgs>("pause"); set => SetDomEventAsync("pause", value); }

    /// <summary>
    ///     Playback is actually running, after any buffering. This, not <c>play</c>, is the event that means the
    ///     user is seeing frames.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/playing_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnPlaying { get => SyncHandler<MediaEventArgs>("playing"); set => SetDomEventSync("playing", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnPlaying"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnPlayingAsync { get => AsyncHandler<MediaEventArgs>("playing"); set => SetDomEventAsync("playing", value); }

    /// <summary>
    ///     Playback reached the end of the media. Does not fire when the user pauses, and does not fire at all on a
    ///     looping element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/ended_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnEnded { get => SyncHandler<MediaEventArgs>("ended"); set => SetDomEventSync("ended", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnEnded"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnEndedAsync { get => AsyncHandler<MediaEventArgs>("ended"); set => SetDomEventAsync("ended", value); }

    /// <summary>
    ///     The playback position moved. Fires only a few times a second and at no guaranteed rate, so it drives a
    ///     progress readout but never a smooth animation — use the animation frame for that.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/timeupdate_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnTimeUpdate { get => SyncHandler<MediaEventArgs>("timeupdate"); set => SetDomEventSync("timeupdate", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnTimeUpdate"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnTimeUpdateAsync { get => AsyncHandler<MediaEventArgs>("timeupdate"); set => SetDomEventAsync("timeupdate", value); }

    /// <summary>
    ///     The volume or the muted state changed. One event covers both, so read whichever you care about rather
    ///     than assuming a volume move.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/volumechange_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnVolumeChange { get => SyncHandler<MediaEventArgs>("volumechange"); set => SetDomEventSync("volumechange", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnVolumeChange"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnVolumeChangeAsync { get => AsyncHandler<MediaEventArgs>("volumechange"); set => SetDomEventAsync("volumechange", value); }

    /// <summary>
    ///     The playback speed changed.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/ratechange_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnRateChange { get => SyncHandler<MediaEventArgs>("ratechange"); set => SetDomEventSync("ratechange", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnRateChange"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnRateChangeAsync { get => AsyncHandler<MediaEventArgs>("ratechange"); set => SetDomEventAsync("ratechange", value); }

    /// <summary>
    ///     The media's duration became known, or changed. Until this has fired the duration is <c>NaN</c>, so a
    ///     progress bar built before it divides by nothing.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/durationchange_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnDurationChange { get => SyncHandler<MediaEventArgs>("durationchange"); set => SetDomEventSync("durationchange", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnDurationChange"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnDurationChangeAsync { get => AsyncHandler<MediaEventArgs>("durationchange"); set => SetDomEventAsync("durationchange", value); }

    /// <summary>
    ///     Duration and dimensions are known and seeking is now possible. The earliest safe point to set a start
    ///     position or size a player to the video's aspect ratio.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/loadedmetadata_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnLoadedMetadata { get => SyncHandler<MediaEventArgs>("loadedmetadata"); set => SetDomEventSync("loadedmetadata", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnLoadedMetadata"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnLoadedMetadataAsync { get => AsyncHandler<MediaEventArgs>("loadedmetadata"); set => SetDomEventAsync("loadedmetadata", value); }

    /// <summary>
    ///     A seek finished and playback is positioned at the new time.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/seeked_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnSeeked { get => SyncHandler<MediaEventArgs>("seeked"); set => SetDomEventSync("seeked", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnSeeked"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnSeekedAsync { get => AsyncHandler<MediaEventArgs>("seeked"); set => SetDomEventAsync("seeked", value); }

    /// <summary>
    ///     A seek started. Pair it with <c>seeked</c> to show a spinner while the new position loads.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/seeking_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnSeeking { get => SyncHandler<MediaEventArgs>("seeking"); set => SetDomEventSync("seeking", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnSeeking"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnSeekingAsync { get => AsyncHandler<MediaEventArgs>("seeking"); set => SetDomEventAsync("seeking", value); }

    /// <summary>
    ///     Playback stalled waiting for more data — this is the buffering state. It is not an error and needs no
    ///     recovery, but it is what a loading indicator should watch.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/waiting_event">MDN</see>
    /// </summary>
    public Action<MediaEventArgs>? OnWaiting { get => SyncHandler<MediaEventArgs>("waiting"); set => SetDomEventSync("waiting", value); }
    /// <summary>
    ///     The <see langword="async"/> form of <see cref="OnWaiting"/>, awaited by the renderer before
    ///     it re-renders.
    ///     <para>
    ///         Wire one or the other, never both: if both are set the synchronous one wins and
    ///         this is silently dropped. RASK027 reports it.
    ///     </para>
    /// </summary>
    public Func<MediaEventArgs, Task>? OnWaitingAsync { get => AsyncHandler<MediaEventArgs>("waiting"); set => SetDomEventAsync("waiting", value); }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendMediaUrlAttr(sb, "src", Src);
        }

        if (Controls is true)
        {
            AppendAttr(sb, "controls", null);
        }

        if (Autoplay is true)
        {
            AppendAttr(sb, "autoplay", null);
        }

        if (Loop is true)
        {
            AppendAttr(sb, "loop", null);
        }

        if (Muted is true)
        {
            AppendAttr(sb, "muted", null);
        }

        if (Preload is not null)
        {
            AppendAttr(sb, "preload", Preload);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }

        // Media events emit after the shared attrs. Early-out in one null check for a media element
        // with no media-event handler wired (the universal events were already emitted by base).
        if (HasDomEvents && LiveRenderContext.CurrentSync is { } ctx)
        {
            foreach (var name in MediaEventOrder)
            {
                EmitDomEvent(sb, ctx, name);
            }
        }
    }
}
