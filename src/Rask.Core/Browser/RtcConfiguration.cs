namespace Rask.Core.Browser;

/// <summary>How a peer connection reaches the other side.</summary>
public sealed record RtcConfiguration
{
    /// <summary>
    ///     STUN/TURN server URLs (<c>stun:</c>, <c>turn:</c> or <c>turns:</c>). Empty — the default — means
    ///     host candidates only, which connects peers on the same machine or LAN but not across the
    ///     internet. Rask ships no STUN or TURN server; supply your own.
    /// </summary>
    public string[]? IceServers { get; init; }

    /// <summary>
    ///     <c>"all"</c> (default) or <c>"relay"</c>. <c>"relay"</c> forces traffic through a TURN server, so
    ///     the peer never learns your local network addresses — the setting to use when that leak matters.
    /// </summary>
    public string? IceTransportPolicy { get; init; }
}
