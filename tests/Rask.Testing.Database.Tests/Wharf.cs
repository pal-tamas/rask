using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Testing.Database.Tests;

/// <summary>
///     A tenant's wharf: its name unique within the tenant, its operator a company of the tenant's — what only the store
///     refuses, which a form tells as the field is left (FormShowsStoreTests).
/// </summary>
public sealed partial class Wharf : Aggregate<Guid, Versioned, Model, PerTenant>
{
    public const string NameTaken = "Már van ilyen név.";

    public const string NoSuchOperator = "No such operator.";

    private Wharf() { } // EF materialization

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public Guid? WharfOperatorId { get; private set; }

    public void Rename(string name) => Name = name;

    public void RunBy(Guid operatorId) => WharfOperatorId = operatorId;

    public static void Configure(EntityTypeBuilder<Wharf> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(NameTaken);
        b.HasOne<WharfOperator>().WithMany().HasForeignKey(x => x.WharfOperatorId).WhenMissing(NoSuchOperator);
    }

    private static Wharf Build(string name) => new() { Id = Guid.CreateVersion7(), Name = name };
}

/// <summary>A tenant's company that runs wharves.</summary>
public sealed class WharfOperator : Aggregate<Guid, PerTenant>
{
    private WharfOperator() { } // EF materialization

    public string Name { get; private set; } = "";

    internal static WharfOperator Named(string name) => new() { Id = Guid.CreateVersion7(), Name = name };
}
