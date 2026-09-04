using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>
/// An account. Derives from ASP.NET Core Identity's user, so it carries the password hash, the
/// security stamp, the lockout counters and the confirmation flags without Rask reimplementing any
/// of them.
/// </summary>
/// <remarks>
/// Add your own columns by deriving from this type and mapping it instead — the ordinary Identity
/// pattern. Rask deliberately has no user type of its own beyond this one: everything the framework
/// reads at render time comes from the <c>ClaimsPrincipal</c>, not from the entity.
/// </remarks>
public class RaskUser : IdentityUser
{
    /// <summary>When the account was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; }
}

/// <summary>The roles a Rask app has out of the box.</summary>
/// <remarks>
/// Two, deliberately: the first account to register is an <see cref="Admin"/> and every account after
/// it is a <see cref="User"/>. Anything richer is an app's own business, and these are plain Identity
/// roles, so adding more is <c>RoleManager.CreateAsync</c>.
/// </remarks>
public static class RaskRoles
{
    /// <summary>The administrator role. Held by the first account to register, and gates <c>/_rask</c>.</summary>
    public const string Admin = "admin";

    /// <summary>The ordinary signed-in role. Held by every account after the first.</summary>
    public const string User = "user";

    /// <summary>Both roles, in the order they are seeded.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, User];
}

/// <summary>Model-building helper for the account tables.</summary>
public static class AuthModelBuilderExtensions
{
    /// <summary>
    /// Maps ASP.NET Core Identity's tables onto the application context. Call from your context's
    /// <c>OnModelCreating</c>, then create the schema with <c>rask db add AddAuth &amp;&amp; rask db update</c>.
    /// </summary>
    /// <remarks>
    /// This is what <c>IdentityDbContext</c> would have configured. Rask maps it explicitly instead so
    /// an app keeps one plain <c>DbContext</c> that every battery adds its own tables to, rather than
    /// having to inherit from Identity's context and give up that base class.
    /// </remarks>
    public static ModelBuilder AddRaskAuth(this ModelBuilder modelBuilder) =>
        modelBuilder.AddRaskAuth<RaskUser>();

    /// <summary>
    /// Maps Identity's tables for an application-supplied user type deriving from <see cref="RaskUser"/>.
    /// </summary>
    /// <typeparam name="TUser">The application's user entity.</typeparam>
    public static ModelBuilder AddRaskAuth<TUser>(this ModelBuilder modelBuilder)
        where TUser : RaskUser
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<TUser>(b =>
        {
            b.ToTable("AspNetUsers");
            b.HasKey(u => u.Id);
            // Unique on the normalized name: this is what makes "already registered" a database
            // guarantee rather than a check-then-insert race between two concurrent registrations.
            b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique();
            b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex");
            b.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(u => u.UserName).HasMaxLength(256);
            b.Property(u => u.NormalizedUserName).HasMaxLength(256);
            b.Property(u => u.Email).HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasMaxLength(256);
            b.HasMany<IdentityUserClaim<string>>().WithOne().HasForeignKey(c => c.UserId).IsRequired();
            b.HasMany<IdentityUserLogin<string>>().WithOne().HasForeignKey(l => l.UserId).IsRequired();
            b.HasMany<IdentityUserToken<string>>().WithOne().HasForeignKey(t => t.UserId).IsRequired();
            b.HasMany<IdentityUserRole<string>>().WithOne().HasForeignKey(r => r.UserId).IsRequired();
        });

        modelBuilder.Entity<IdentityRole>(b =>
        {
            b.ToTable("AspNetRoles");
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.NormalizedName).HasDatabaseName("RoleNameIndex").IsUnique();
            b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
            b.Property(r => r.Name).HasMaxLength(256);
            b.Property(r => r.NormalizedName).HasMaxLength(256);
            b.HasMany<IdentityUserRole<string>>().WithOne().HasForeignKey(r => r.RoleId).IsRequired();
            b.HasMany<IdentityRoleClaim<string>>().WithOne().HasForeignKey(c => c.RoleId).IsRequired();
        });

        modelBuilder.Entity<IdentityUserClaim<string>>(b =>
        {
            b.ToTable("AspNetUserClaims");
            b.HasKey(c => c.Id);
        });

        modelBuilder.Entity<IdentityRoleClaim<string>>(b =>
        {
            b.ToTable("AspNetRoleClaims");
            b.HasKey(c => c.Id);
        });

        modelBuilder.Entity<IdentityUserRole<string>>(b =>
        {
            b.ToTable("AspNetUserRoles");
            b.HasKey(r => new { r.UserId, r.RoleId });
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(b =>
        {
            b.ToTable("AspNetUserLogins");
            b.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            b.Property(l => l.LoginProvider).HasMaxLength(128);
            b.Property(l => l.ProviderKey).HasMaxLength(128);
        });

        modelBuilder.Entity<IdentityUserToken<string>>(b =>
        {
            b.ToTable("AspNetUserTokens");
            b.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            b.Property(t => t.LoginProvider).HasMaxLength(128);
            b.Property(t => t.Name).HasMaxLength(128);
        });

        // The one row that makes "the first account is the administrator" a database guarantee.
        modelBuilder.ApplyConfiguration(new AuthInstanceClaimConfiguration());

        return modelBuilder;
    }
}
