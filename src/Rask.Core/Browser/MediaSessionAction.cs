namespace Rask.Core.Browser;

/// <summary>
///     A media control the OS can raise (a media key, lock-screen button, or headset gesture) — the
///     actions of the <see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaSession/setActionHandler">
///     Media Session API</see>.
/// </summary>
public enum MediaSessionAction
{
    /// <summary>Resume / start playback.</summary>
    Play,

    /// <summary>Pause playback.</summary>
    Pause,

    /// <summary>Stop playback.</summary>
    Stop,

    /// <summary>Skip to the next track.</summary>
    NextTrack,

    /// <summary>Skip to the previous track.</summary>
    PreviousTrack,

    /// <summary>Seek backward by a short interval.</summary>
    SeekBackward,

    /// <summary>Seek forward by a short interval.</summary>
    SeekForward
}
