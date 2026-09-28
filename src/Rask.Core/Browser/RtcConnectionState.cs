namespace Rask.Core.Browser;

/// <summary>The lifecycle of a peer connection (<c>RTCPeerConnection.connectionState</c>).</summary>
public enum RtcConnectionState
{
    /// <summary>Created, but no ICE work has started.</summary>
    New,

    /// <summary>Connectivity checks are in progress.</summary>
    Connecting,

    /// <summary>Usable — media and data can flow.</summary>
    Connected,

    /// <summary>Connectivity lost; may recover on its own.</summary>
    Disconnected,

    /// <summary>Connectivity lost for good.</summary>
    Failed,

    /// <summary>Closed, by either side.</summary>
    Closed
}
