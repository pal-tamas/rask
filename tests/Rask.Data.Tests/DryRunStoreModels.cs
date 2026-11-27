using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>
///     A tenant's slipway: its name unique within the tenant, and of a company of the tenant's — what only the store
///     refuses, which a dry run tells (DryRunStoreTests).
/// </summary>
public sealed partial class Slipway : Aggregate<Guid>, IVersioned, IDeletable, IBelongsTo<GuidShop>
{
    internal const string NameTaken = "Már van ilyen név.";

    internal const string InUse = "A hawser still uses this slipway.";

    private Slipway() { } // EF materialization

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public Guid? RefCompanyId { get; private set; }

    public void Rename(string name) => Name = name;

    public void LetTo(Guid? refCompanyId) => RefCompanyId = refCompanyId;

    public static void Configure(EntityTypeBuilder<Slipway> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(NameTaken);
        b.HasOne<RefCompany>().WithMany().HasForeignKey(x => x.RefCompanyId);
        b.WhenReferenced(InUse);
    }

    private static Slipway Open(string name) => new() { Id = Guid.CreateVersion7(), Name = name };
}

/// <summary>A boat moored at a slipway, which keeps the slipway from being deleted.</summary>
public sealed class Hawser : Aggregate<Guid>, IBelongsTo<GuidShop>
{
    private Hawser() { } // EF materialization

    public Guid SlipwayId { get; private set; }

    internal static Hawser At(Guid slipway) => new() { Id = Guid.CreateVersion7(), SlipwayId = slipway };

    public static void Configure(EntityTypeBuilder<Hawser> b) =>
        b.HasOne<Slipway>().WithMany().HasForeignKey(x => x.SlipwayId).OnDelete(DeleteBehavior.Restrict);
}
