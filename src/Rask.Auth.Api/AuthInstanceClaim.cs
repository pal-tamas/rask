using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Auth;

/// <summary>
/// The single row that records this instance has been claimed, and by whom.
/// </summary>
/// <remarks>
/// <para>
/// Its whole purpose is <see cref="Id"/>: the primary key is a constant, so the table holds at most one
/// row and the <b>second</b> insert fails at the database rather than succeeding quietly. That is what
/// makes "the first account to register is the administrator" a guarantee instead of a race — two
/// registrations arriving together cannot both read an empty user table and both award themselves the
/// admin role, because only one of them can insert this row.
/// </para>
/// <para>
/// A count of the users table would not do. Counting and then inserting is two statements, and whether
/// a concurrent pair can interleave between them depends on the provider's isolation level and, on
/// SQLite, on whether the transaction happened to take its write lock early — a contended COMMIT can
/// roll itself back. A primary key means the same thing on every provider, which is also why this is
/// not written as a SQLite-specific <c>BEGIN IMMEDIATE</c>.
/// </para>
/// <para>
/// It outlives the accounts, deliberately. If every user is later deleted, the instance stays claimed
/// and the next person to register is an ordinary user — a second land-grab window is not something a
/// delete should be able to open.
/// </para>
/// </remarks>
public sealed class AuthInstanceClaim
{
    /// <summary>The only value this column ever holds.</summary>
    public const int SingletonId = 1;

    /// <summary>The primary key, always <see cref="SingletonId"/>.</summary>
    public int Id { get; set; } = SingletonId;

    /// <summary>The id of the account that claimed this instance and holds the admin role.</summary>
    public string AdminUserId { get; set; } = "";

    /// <summary>When it was claimed (UTC).</summary>
    public DateTime ClaimedUtc { get; set; }
}

/// <summary>The EF Core mapping for <see cref="AuthInstanceClaim"/>.</summary>
public sealed class AuthInstanceClaimConfiguration : IEntityTypeConfiguration<AuthInstanceClaim>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AuthInstanceClaim> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.ToTable("RaskAuthInstanceClaim");

        // Never generated: the value is the constraint. A database-assigned key would let every
        // registration insert a new row, which is exactly the guarantee this table exists to provide.
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();

        entity.Property(x => x.AdminUserId).HasMaxLength(256).IsRequired();
    }
}
