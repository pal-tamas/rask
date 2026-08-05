namespace Rask.Dashboard;

/// <summary>
/// Whether the dashboard ended up on its own fallback authorization policy or on one the application
/// defined. The layout reads this to decide whether to warn — a banner that appears even when the app has
/// configured real access control would be noise, and noise is how a genuine warning gets ignored.
/// </summary>
/// <remarks>
/// Written once, from the <c>AuthorizationOptions</c> post-configure callback, before the first request is
/// served; read-only from then on.
/// </remarks>
public sealed class DashboardSecurityState
{
    /// <summary>
    /// <c>true</c> when no <see cref="RaskDashboardPolicies.Access"/> policy was defined and the dashboard
    /// supplied its own.
    /// </summary>
    public bool UsingFallbackPolicy { get; internal set; }

    /// <summary>
    /// <c>true</c> when that fallback lets everyone in — Development, or
    /// <see cref="RaskDashboardOptions.AllowAnonymousAccess"/>. <c>false</c> means it denies everyone.
    /// </summary>
    public bool FallbackIsOpen { get; internal set; }

    /// <summary>The dashboard is reachable by anyone because nothing was configured.</summary>
    public bool IsUnsecured => UsingFallbackPolicy && FallbackIsOpen;
}
