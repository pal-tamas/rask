namespace Rask.Core.Authorization;

public enum RouteAuthorizationOutcome
{
    Allow,
    Challenge,
    Forbid
}

public sealed class RouteAuthorizationResult
{
    private RouteAuthorizationResult(
        RouteAuthorizationOutcome outcome,
        string? authenticationScheme,
        Type? failedOnPage)
    {
        Outcome = outcome;
        AuthenticationScheme = authenticationScheme;
        FailedOnPage = failedOnPage;
    }

    public RouteAuthorizationOutcome Outcome { get; }
    public string? AuthenticationScheme { get; }
    public Type? FailedOnPage { get; }

    public static RouteAuthorizationResult Allow() =>
        new(RouteAuthorizationOutcome.Allow, null, null);

    public static RouteAuthorizationResult Challenge(string? scheme, Type? page) =>
        new(RouteAuthorizationOutcome.Challenge, scheme, page);

    public static RouteAuthorizationResult Forbid(string? scheme, Type? page) =>
        new(RouteAuthorizationOutcome.Forbid, scheme, page);
}
