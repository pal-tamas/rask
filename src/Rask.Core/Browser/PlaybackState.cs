namespace Rask.Core.Browser;

/// <summary>Playback state reported to the OS (the <c>playbackState</c> of the media session).</summary>
public enum PlaybackState
{
    /// <summary>No active media (<c>"none"</c>).</summary>
    None,

    /// <summary>Media is paused (<c>"paused"</c>).</summary>
    Paused,

    /// <summary>Media is playing (<c>"playing"</c>).</summary>
    Playing
}
