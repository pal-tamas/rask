using System.Text;
using Rask.Core.Live;

namespace Rask.Html.Components;

// Shared base for the media elements (Audio, Video), mirroring the DOM `HTMLMediaElement`
// interface. Carries the attributes common to every media element so the concrete tags don't
// each redeclare them; tag-specific extras (e.g. Video's poster/width/height/playsinline) stay
// on the subclass and emit after the shared block (base.WriteAttributes runs first).
//
// It also adds the HTMLMediaElement-specific EVENTS (play/pause/timeupdate/…) — these are not part of
// the universal GlobalEventHandlers surface, so they live here rather than on Element. They flow through
// the same LiveState DomEvents store (sync + async `Action<MediaEventArgs>` / `Func<MediaEventArgs, Task>`
// pairs, see ElementEvents) and emit after the shared attrs.
public abstract partial class HtmlMediaElement : Element
{
    // Emit order for the media events, kept deterministic like Element's GlobalEventOrder.
    private static readonly string[] MediaEventOrder =
    {
        "play", "pause", "playing", "ended", "timeupdate", "volumechange", "ratechange", "durationchange",
        "loadedmetadata", "seeked", "seeking", "waiting"
    };

    public string? Src { get; set; }
    public bool? Controls { get; set; }
    public bool? Autoplay { get; set; }
    public bool? Loop { get; set; }
    public bool? Muted { get; set; }
    public string? Preload { get; set; }
    public string? CrossOrigin { get; set; }

    public Action<MediaEventArgs>? OnPlay { get => SyncHandler<MediaEventArgs>("play"); set => SetDomEventSync("play", value); }
    public Func<MediaEventArgs, Task>? OnPlayAsync { get => AsyncHandler<MediaEventArgs>("play"); set => SetDomEventAsync("play", value); }

    public Action<MediaEventArgs>? OnPause { get => SyncHandler<MediaEventArgs>("pause"); set => SetDomEventSync("pause", value); }
    public Func<MediaEventArgs, Task>? OnPauseAsync { get => AsyncHandler<MediaEventArgs>("pause"); set => SetDomEventAsync("pause", value); }

    public Action<MediaEventArgs>? OnPlaying { get => SyncHandler<MediaEventArgs>("playing"); set => SetDomEventSync("playing", value); }
    public Func<MediaEventArgs, Task>? OnPlayingAsync { get => AsyncHandler<MediaEventArgs>("playing"); set => SetDomEventAsync("playing", value); }

    public Action<MediaEventArgs>? OnEnded { get => SyncHandler<MediaEventArgs>("ended"); set => SetDomEventSync("ended", value); }
    public Func<MediaEventArgs, Task>? OnEndedAsync { get => AsyncHandler<MediaEventArgs>("ended"); set => SetDomEventAsync("ended", value); }

    public Action<MediaEventArgs>? OnTimeUpdate { get => SyncHandler<MediaEventArgs>("timeupdate"); set => SetDomEventSync("timeupdate", value); }
    public Func<MediaEventArgs, Task>? OnTimeUpdateAsync { get => AsyncHandler<MediaEventArgs>("timeupdate"); set => SetDomEventAsync("timeupdate", value); }

    public Action<MediaEventArgs>? OnVolumeChange { get => SyncHandler<MediaEventArgs>("volumechange"); set => SetDomEventSync("volumechange", value); }
    public Func<MediaEventArgs, Task>? OnVolumeChangeAsync { get => AsyncHandler<MediaEventArgs>("volumechange"); set => SetDomEventAsync("volumechange", value); }

    public Action<MediaEventArgs>? OnRateChange { get => SyncHandler<MediaEventArgs>("ratechange"); set => SetDomEventSync("ratechange", value); }
    public Func<MediaEventArgs, Task>? OnRateChangeAsync { get => AsyncHandler<MediaEventArgs>("ratechange"); set => SetDomEventAsync("ratechange", value); }

    public Action<MediaEventArgs>? OnDurationChange { get => SyncHandler<MediaEventArgs>("durationchange"); set => SetDomEventSync("durationchange", value); }
    public Func<MediaEventArgs, Task>? OnDurationChangeAsync { get => AsyncHandler<MediaEventArgs>("durationchange"); set => SetDomEventAsync("durationchange", value); }

    public Action<MediaEventArgs>? OnLoadedMetadata { get => SyncHandler<MediaEventArgs>("loadedmetadata"); set => SetDomEventSync("loadedmetadata", value); }
    public Func<MediaEventArgs, Task>? OnLoadedMetadataAsync { get => AsyncHandler<MediaEventArgs>("loadedmetadata"); set => SetDomEventAsync("loadedmetadata", value); }

    public Action<MediaEventArgs>? OnSeeked { get => SyncHandler<MediaEventArgs>("seeked"); set => SetDomEventSync("seeked", value); }
    public Func<MediaEventArgs, Task>? OnSeekedAsync { get => AsyncHandler<MediaEventArgs>("seeked"); set => SetDomEventAsync("seeked", value); }

    public Action<MediaEventArgs>? OnSeeking { get => SyncHandler<MediaEventArgs>("seeking"); set => SetDomEventSync("seeking", value); }
    public Func<MediaEventArgs, Task>? OnSeekingAsync { get => AsyncHandler<MediaEventArgs>("seeking"); set => SetDomEventAsync("seeking", value); }

    public Action<MediaEventArgs>? OnWaiting { get => SyncHandler<MediaEventArgs>("waiting"); set => SetDomEventSync("waiting", value); }
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
