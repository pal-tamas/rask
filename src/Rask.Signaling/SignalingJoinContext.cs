namespace Rask.Signaling;

/// <summary>What <see cref="SignalingOptions.AuthorizeRoom" /> is given to make its decision.</summary>
/// <param name="Room">The room the caller is asking to join, verbatim as the client sent it.</param>
/// <param name="User">The authenticated caller.</param>
/// <param name="Services">The request's service provider, for looking up whatever the decision needs.</param>
public readonly record struct SignalingJoinContext(
    string Room,
    System.Security.Claims.ClaimsPrincipal User,
    IServiceProvider Services);
