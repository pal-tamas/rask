using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Auth;

/// <summary>The EF Core mapping for <see cref="AuthInstanceClaim"/>.</summary>
public sealed class AuthInstanceClaimConfiguration : IEntityTypeConfiguration<AuthInstanceClaim>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<AuthInstanceClaim> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RaskAuthInstanceClaim");

        // Never generated: the value is the constraint. A database-assigned key would let every
        // registration insert a new row, which is exactly the guarantee this table exists to provide.
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
    }
}
