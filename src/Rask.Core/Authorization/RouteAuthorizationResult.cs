namespace Rask.Core.Authorization;

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

    /// <summary>Where the reader is sent instead, from the app's root, when the outcome is <see cref="RouteAuthorizationOutcome.Redirect" />.</summary>
    public string? RedirectTo { get; private init; }

    public static RouteAuthorizationResult Allow() =>
        new(RouteAuthorizationOutcome.Allow, null, null);

    public static RouteAuthorizationResult Challenge(string? scheme, Type? page) =>
        new(RouteAuthorizationOutcome.Challenge, scheme, page);

    public static RouteAuthorizationResult Forbid(string? scheme, Type? page) =>
        new(RouteAuthorizationOutcome.Forbid, scheme, page);

    /// <summary>Sends the reader to <paramref name="url" />, a path from the app's root, instead of the page.</summary>
    public static RouteAuthorizationResult Redirect(string url) =>
        new(RouteAuthorizationOutcome.Redirect, null, null) { RedirectTo = url };
}
