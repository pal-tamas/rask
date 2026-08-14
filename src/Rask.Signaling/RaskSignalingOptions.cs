namespace Rask.Signaling;

/// <summary>
///     How the WebRTC signaling endpoint behaves. Every limit here exists because the endpoint is a
///     <b>relay between untrusted peers</b>: whatever one client sends, another client receives.
/// </summary>
public sealed class RaskSignalingOptions
{
    /// <summary>
    ///     The path the signaling socket is served from. Separate from the live render socket on purpose —
    ///     that one has its own frame contract, rate limits and shutdown-drain semantics.
    /// </summary>
    public string Path { get; set; } = "/rask/signaling";

    /// <summary>
    ///     Whether a caller must be authenticated. <c>true</c> by default: a signaling relay that anyone can
    ///     join is a way to reach other people's browsers, and a public default would make that the
    ///     accident rather than the decision. Set <c>false</c> only for a genuinely open room.
    /// </summary>
    public bool RequireAuthorization { get; set; } = true;

    /// <summary>
    ///     Decides whether this caller may join this room. Return <c>false</c> to refuse. Runs after
    ///     authentication, once per join, and is the hook for "is this user a member of this conversation" —
    ///     the question the framework cannot answer for you. The default allows any authenticated caller
    ///     into any room, which is only appropriate when every user of the app may talk to every other.
    /// </summary>
    public Func<SignalingJoinContext, ValueTask<bool>> AuthorizeRoom { get; set; } = _ => ValueTask.FromResult(true);

    /// <summary>Largest single inbound message, in bytes. Anything larger closes the socket.</summary>
    public int MaxMessageBytes { get; set; } = 64 * 1024;

    /// <summary>
    ///     Largest signaling payload one peer may relay to another, in bytes. Smaller than
    ///     <see cref="MaxMessageBytes" /> because an SDP offer is a few kilobytes and an ICE candidate is a
    ///     few hundred bytes — this is the cap that stops the relay being used as a free message bus.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 16 * 1024;

    /// <summary>Inbound messages per second, per peer, before the socket is closed. 0 disables the limit.</summary>
    public int MaxMessagesPerSecond { get; set; } = 100;

    /// <summary>
    ///     Most peers allowed in one room. A mesh of N peers costs N² connections, so this is a real
    ///     resource bound, not a formality. A join beyond it is refused.
    /// </summary>
    public int MaxPeersPerRoom { get; set; } = 8;

    /// <summary>Most rooms held at once. A join that would exceed it is refused.</summary>
    public int MaxRooms { get; set; } = 1000;

    /// <summary>Longest a room id may be. Room ids are opaque to the framework but are held in memory.</summary>
    public int MaxRoomIdLength { get; set; } = 128;
}

/// <summary>What <see cref="RaskSignalingOptions.AuthorizeRoom" /> is given to make its decision.</summary>
/// <param name="Room">The room the caller is asking to join, verbatim as the client sent it.</param>
/// <param name="User">The authenticated caller.</param>
/// <param name="Services">The request's service provider, for looking up whatever the decision needs.</param>
public readonly record struct SignalingJoinContext(
    string Room,
    System.Security.Claims.ClaimsPrincipal User,
    IServiceProvider Services);
