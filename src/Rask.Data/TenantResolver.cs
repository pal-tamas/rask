namespace Rask.Data;

/// <summary>
///     The application's answer to "which tenant is this work for", as <c>AddRaskTenant</c> registered it.
/// </summary>
/// <remarks>
///     A singleton that only holds the function. What it returns belongs to one request or one live session,
///     so it is called through <see cref="ResolvedTenant" />, which is scoped and keeps the answer.
/// </remarks>
internal sealed class TenantResolver(Func<IServiceProvider, Guid?> resolve)
{
    /// <summary>Asks the application, handing it the services of the request or session in flight.</summary>
    internal Guid? Resolve(IServiceProvider scope) => resolve(scope);
}
