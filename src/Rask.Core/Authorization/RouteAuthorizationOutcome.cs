namespace Rask.Core.Authorization;

public enum RouteAuthorizationOutcome
{
    Allow,
    Challenge,
    Forbid,

    /// <summary>The page is not for this reader, who is sent on to <see cref="RouteAuthorizationResult.RedirectTo" />.</summary>
    Redirect,
}
