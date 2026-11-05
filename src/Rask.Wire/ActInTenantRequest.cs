namespace Rask.Wire;

/// <summary>The tenant a signed-in administrator acts in from now on, on this sign-in.</summary>
/// <param name="Tenant">
/// Its id — a <see cref="System.Guid" />, or a tenant's number — or <see langword="null" /> to act in none again.
/// </param>
public sealed record ActInTenantRequest(string? Tenant);
