namespace Rask.Core.Browser;

/// <summary>One end of the offer/answer exchange — an SDP blob plus its role.</summary>
/// <param name="Type"><c>"offer"</c> or <c>"answer"</c>.</param>
/// <param name="Sdp">The session description.</param>
public sealed record RtcDescription(string Type, string Sdp);
