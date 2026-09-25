namespace Rask.Core.Browser;

/// <summary>One ICE candidate, to be handed to the other peer by your signaling channel.</summary>
/// <param name="Candidate">The candidate line.</param>
/// <param name="SdpMid">The media stream identification this candidate belongs to.</param>
/// <param name="SdpMLineIndex">The index of the media description this candidate belongs to.</param>
public sealed record RtcIceCandidate(string Candidate, string? SdpMid, int? SdpMLineIndex);
