using Rask.Data;

namespace Rask.Auth.TypedId.Tests;

/// <summary>The tenant's id: a typed id over a Guid, as TA's and Eszaf's are once they move off their own.</summary>
/// <param name="Value">The Guid it holds.</param>
public readonly partial record struct TenantId(Guid Value);

/// <summary>The account's id: a typed id over a Guid.</summary>
/// <param name="Value">The Guid it holds.</param>
public readonly partial record struct UserId(Guid Value);

/// <summary>A company the app keeps apart from the others.</summary>
public sealed class Tenant : Aggregate<TenantId>, ITenant
{
    /// <summary>The company's name.</summary>
    public string Name { get; private set; } = "";

    /// <summary>An empty row, for a test that writes one as a table holds it.</summary>
    /// <returns>A company with nothing set.</returns>
    public static Tenant Blank() => new();
}

/// <summary>The owner-approved shape: <c>User : Authenticatable&lt;UserId&gt;, IBelongsTo&lt;Tenant&gt;</c>.</summary>
public sealed partial class User : Authenticatable<UserId>, IBelongsTo<Tenant>
{
    /// <summary>The company the user works for, or none for an administrator.</summary>
    public TenantId? TenantId { get; private set; }

    /// <summary>Moves the user into <paramref name="tenant" />.</summary>
    /// <param name="tenant">The company.</param>
    public void JoinTenant(TenantId tenant) => TenantId = tenant;
}
