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
// the same LiveState DomEvents store (Callback<MediaEventArgs> pairs) and emit after the shared attrs.
public abstract class HtmlMediaElement : Element
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

    public Callback<MediaEventArgs>? OnPlay { get => GetDomEvent("play") as Callback<MediaEventArgs>; set => SetDomEventSync("play", value); }
    public CallbackAsync<MediaEventArgs>? OnPlayAsync { get => GetDomEvent("play") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("play", value); }

    public Callback<MediaEventArgs>? OnPause { get => GetDomEvent("pause") as Callback<MediaEventArgs>; set => SetDomEventSync("pause", value); }
    public CallbackAsync<MediaEventArgs>? OnPauseAsync { get => GetDomEvent("pause") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("pause", value); }

    public Callback<MediaEventArgs>? OnPlaying { get => GetDomEvent("playing") as Callback<MediaEventArgs>; set => SetDomEventSync("playing", value); }
    public CallbackAsync<MediaEventArgs>? OnPlayingAsync { get => GetDomEvent("playing") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("playing", value); }

    public Callback<MediaEventArgs>? OnEnded { get => GetDomEvent("ended") as Callback<MediaEventArgs>; set => SetDomEventSync("ended", value); }
    public CallbackAsync<MediaEventArgs>? OnEndedAsync { get => GetDomEvent("ended") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("ended", value); }

    public Callback<MediaEventArgs>? OnTimeUpdate { get => GetDomEvent("timeupdate") as Callback<MediaEventArgs>; set => SetDomEventSync("timeupdate", value); }
    public CallbackAsync<MediaEventArgs>? OnTimeUpdateAsync { get => GetDomEvent("timeupdate") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("timeupdate", value); }

    public Callback<MediaEventArgs>? OnVolumeChange { get => GetDomEvent("volumechange") as Callback<MediaEventArgs>; set => SetDomEventSync("volumechange", value); }
    public CallbackAsync<MediaEventArgs>? OnVolumeChangeAsync { get => GetDomEvent("volumechange") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("volumechange", value); }

    public Callback<MediaEventArgs>? OnRateChange { get => GetDomEvent("ratechange") as Callback<MediaEventArgs>; set => SetDomEventSync("ratechange", value); }
    public CallbackAsync<MediaEventArgs>? OnRateChangeAsync { get => GetDomEvent("ratechange") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("ratechange", value); }

    public Callback<MediaEventArgs>? OnDurationChange { get => GetDomEvent("durationchange") as Callback<MediaEventArgs>; set => SetDomEventSync("durationchange", value); }
    public CallbackAsync<MediaEventArgs>? OnDurationChangeAsync { get => GetDomEvent("durationchange") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("durationchange", value); }

    public Callback<MediaEventArgs>? OnLoadedMetadata { get => GetDomEvent("loadedmetadata") as Callback<MediaEventArgs>; set => SetDomEventSync("loadedmetadata", value); }
    public CallbackAsync<MediaEventArgs>? OnLoadedMetadataAsync { get => GetDomEvent("loadedmetadata") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("loadedmetadata", value); }

    public Callback<MediaEventArgs>? OnSeeked { get => GetDomEvent("seeked") as Callback<MediaEventArgs>; set => SetDomEventSync("seeked", value); }
    public CallbackAsync<MediaEventArgs>? OnSeekedAsync { get => GetDomEvent("seeked") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("seeked", value); }

    public Callback<MediaEventArgs>? OnSeeking { get => GetDomEvent("seeking") as Callback<MediaEventArgs>; set => SetDomEventSync("seeking", value); }
    public CallbackAsync<MediaEventArgs>? OnSeekingAsync { get => GetDomEvent("seeking") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("seeking", value); }

    public Callback<MediaEventArgs>? OnWaiting { get => GetDomEvent("waiting") as Callback<MediaEventArgs>; set => SetDomEventSync("waiting", value); }
    public CallbackAsync<MediaEventArgs>? OnWaitingAsync { get => GetDomEvent("waiting") as CallbackAsync<MediaEventArgs>; set => SetDomEventAsync("waiting", value); }

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
