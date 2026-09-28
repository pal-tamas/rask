namespace Rask.Dashboard;

/// <summary>The authorization policy the dashboard's pages are gated on.</summary>
public static class RaskDashboardPolicies
{
    /// <summary>
    /// The policy name every dashboard page carries. Define it in your own <c>AddAuthorization</c> to say
    /// who may operate the app:
    /// <code>
    /// builder.Services.AddAuthorization(o =>
    ///     o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
    /// </code>
    /// If you don't, the dashboard supplies a default — permissive in Development, <b>deny-all</b>
    /// everywhere else.
    /// </summary>
    public const string Access = "RaskDashboard";
}
