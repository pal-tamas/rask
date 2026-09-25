namespace Rask.Core.Browser;

/// <summary>Tuning for a data channel (the <c>RTCDataChannelInit</c> options).</summary>
public sealed record RtcDataChannelOptions
{
    /// <summary>Whether messages arrive in the order they were sent. Defaults to <c>true</c>.</summary>
    public bool? Ordered { get; init; }

    /// <summary>How many times to retry a lost message before giving up. <c>null</c> retries indefinitely.</summary>
    public int? MaxRetransmits { get; init; }

    /// <summary>An application-defined sub-protocol name, echoed to the peer.</summary>
    public string? Protocol { get; init; }
}
