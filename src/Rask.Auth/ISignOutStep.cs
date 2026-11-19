namespace Rask.Auth;

/// <summary>
///     Something another battery does as a user signs out on the Server host, while that user is still current and before
///     the cookie goes: Rask.Server's hands the browser's push subscription back (<c>Push.Release</c>). Optional — with no
///     such battery there is none, and sign-out is as it was.
/// </summary>
internal interface ISignOutStep
{
    Task BeforeSignOut();
}
