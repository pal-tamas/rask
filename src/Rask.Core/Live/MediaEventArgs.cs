using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the HTMLMediaElement events (play, pause, ended, timeupdate, volumechange,
// loadedmetadata, durationchange, ratechange, seeked, waiting, playing). The client snapshots the
// media element's playback state at the moment the event fires so a handler can update a transcript,
// progress bar, or play/pause button without reaching back through JS interop. Duration is 0 while
// unknown (the element reports NaN/Infinity before metadata loads — the client normalises those to 0).
public sealed record MediaEventArgs(
    double CurrentTime,
    double Duration,
    bool Paused,
    bool Ended,
    double Volume,
    bool Muted,
    double PlaybackRate)
{
    internal static MediaEventArgs FromJson(JsonElement p) => new(
        EventPayload.ReadDouble(p, "currentTime"),
        EventPayload.ReadDouble(p, "duration"),
        EventPayload.ReadBool(p, "paused"),
        EventPayload.ReadBool(p, "ended"),
        EventPayload.ReadDouble(p, "volume"),
        EventPayload.ReadBool(p, "muted"),
        EventPayload.ReadDouble(p, "playbackRate"));
}
