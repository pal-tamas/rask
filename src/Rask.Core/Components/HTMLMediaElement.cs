using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

// The media EVENTS of MDN's HTMLMediaElement (generated, with every media attribute): play/pause/timeupdate/…
// are not part of the universal GlobalEventHandlers surface, so they live here rather than on Element. They
// flow through the same LiveState DomEvents store (see ElementEvents) and emit after the generated attributes.
public abstract partial class HTMLMediaElement
{
    // Emit order for the media events, kept deterministic like Element's GlobalEventOrder.
    private static readonly string[] MediaEventOrder =
    {
        "play", "pause", "playing", "ended", "timeupdate", "volumechange", "ratechange", "durationchange",
        "loadedmetadata", "seeked", "seeking", "waiting"
    };








    /// <summary>
    ///     Playback was requested — the moment <c>play()</c> was called or the user hit play, which is
    ///     <em>before</em> a single frame has been shown. For the point where it is actually running, use
    ///     <c>playing</c>.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/play_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnPlay { get => Handler<MediaEventArgs>("play"); set => SetHandler("play", value?.Handler); }

    /// <summary>
    ///     Playback was paused. Fires on a real pause, not at the end of the media — <c>ended</c> covers that.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/pause_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnPause { get => Handler<MediaEventArgs>("pause"); set => SetHandler("pause", value?.Handler); }

    /// <summary>
    ///     Playback is actually running, after any buffering. This, not <c>play</c>, is the event that means the
    ///     user is seeing frames.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/playing_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnPlaying { get => Handler<MediaEventArgs>("playing"); set => SetHandler("playing", value?.Handler); }

    /// <summary>
    ///     Playback reached the end of the media. Does not fire when the user pauses, and does not fire at all on a
    ///     looping element.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/ended_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnEnded { get => Handler<MediaEventArgs>("ended"); set => SetHandler("ended", value?.Handler); }

    /// <summary>
    ///     The playback position moved. Fires only a few times a second and at no guaranteed rate, so it drives a
    ///     progress readout but never a smooth animation — use the animation frame for that.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/timeupdate_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnTimeUpdate { get => Handler<MediaEventArgs>("timeupdate"); set => SetHandler("timeupdate", value?.Handler); }

    /// <summary>
    ///     The volume or the muted state changed. One event covers both, so read whichever you care about rather
    ///     than assuming a volume move.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/volumechange_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnVolumeChange { get => Handler<MediaEventArgs>("volumechange"); set => SetHandler("volumechange", value?.Handler); }

    /// <summary>
    ///     The playback speed changed.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/ratechange_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnRateChange { get => Handler<MediaEventArgs>("ratechange"); set => SetHandler("ratechange", value?.Handler); }

    /// <summary>
    ///     The media's duration became known, or changed. Until this has fired the duration is <c>NaN</c>, so a
    ///     progress bar built before it divides by nothing.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/durationchange_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnDurationChange { get => Handler<MediaEventArgs>("durationchange"); set => SetHandler("durationchange", value?.Handler); }

    /// <summary>
    ///     Duration and dimensions are known and seeking is now possible. The earliest safe point to set a start
    ///     position or size a player to the video's aspect ratio.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/loadedmetadata_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnLoadedMetadata { get => Handler<MediaEventArgs>("loadedmetadata"); set => SetHandler("loadedmetadata", value?.Handler); }

    /// <summary>
    ///     A seek finished and playback is positioned at the new time.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/seeked_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnSeeked { get => Handler<MediaEventArgs>("seeked"); set => SetHandler("seeked", value?.Handler); }

    /// <summary>
    ///     A seek started. Pair it with <c>seeked</c> to show a spinner while the new position loads.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/seeking_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnSeeking { get => Handler<MediaEventArgs>("seeking"); set => SetHandler("seeking", value?.Handler); }

    /// <summary>
    ///     Playback stalled waiting for more data — this is the buffering state. It is not an error and needs no
    ///     recovery, but it is what a loading indicator should watch.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLMediaElement/waiting_event">MDN</see>
    /// </summary>
    public Callback<MediaEventArgs>? OnWaiting { get => Handler<MediaEventArgs>("waiting"); set => SetHandler("waiting", value?.Handler); }

    partial void WriteOwnedAttributes(StringBuilder sb)
    {
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
