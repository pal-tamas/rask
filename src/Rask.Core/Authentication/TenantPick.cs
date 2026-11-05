namespace Rask.Core.Authentication;

/// <summary>
/// The tenant a live handler chose for an app with its own sign-in — a <see cref="Guid" /> or a tenant's number, as
/// text; <see langword="null" /> to act in none — carried by the sign-in relay to the app's tenant choice.
/// </summary>
internal sealed record TenantPick(string? Tenant);
