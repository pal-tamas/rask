using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>The roles a Rask app has out of the box.</summary>
/// <remarks>
/// Two, deliberately: the first account to register is an <see cref="Admin"/> and every account after it is a
/// <see cref="User"/>. Anything richer is the app's own: a role is just a name a user holds, so granting a new one is
/// <c>User.UpdateAsync(id, u =&gt; u.GrantRole("editor"))</c>.
/// </remarks>
public static class RaskRoles
{
    /// <summary>The administrator role. Held by the first account to register, and gates <c>/_rask</c>.</summary>
    public const string Admin = "admin";

    /// <summary>The ordinary signed-in role. Held by every account after the first.</summary>
    public const string User = "user";

    /// <summary>Both roles.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, User];
}

/// <summary>Model-building helper for the account tables.</summary>
public static class AuthModelBuilderExtensions
{
    /// <summary>
    /// Maps the accounts: the app's user, its sessions, and the instance claim. Call from your context's
    /// <c>OnModelCreating</c>, before <c>ApplyRaskConventions()</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same model builder.</returns>
    public static ModelBuilder AddRaskAuth(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The app's user type, found at compile time. Nothing mapped when it declared none: an app with no user has no
        // accounts, and mapping tables for a type that does not exist would be a schema nobody asked for.
        return AuthUser.Binding?.Map(modelBuilder) ?? modelBuilder;
    }

    /// <summary>
    /// Maps the accounts for a named user type, when an app would rather say which than let the generator find it.
    /// </summary>
    /// <typeparam name="TUser">The application's user aggregate.</typeparam>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same model builder.</returns>
    public static ModelBuilder AddRaskAuth<TUser>(this ModelBuilder modelBuilder)
        where TUser : Authenticatable
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<TUser>(b =>
        {
            // Unique on the normalized address: this is what makes "already registered" a database guarantee rather than a
            // check-then-insert race between two registrations arriving together.
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
            b.PrimitiveCollection(u => u.Roles);
        });

        modelBuilder.Entity<Session>(b =>
        {
            b.ToTable("RaskAuthSession");
            b.HasIndex(s => s.UserId);
            b.Property(s => s.IpAddress).HasMaxLength(Session.IpAddressLength);
            b.Property(s => s.UserAgent).HasMaxLength(Session.UserAgentLength);
        });

        modelBuilder.Entity<Passkey>(b =>
        {
            b.ToTable("RaskAuthPasskey");
            b.HasIndex(p => p.UserId);

            // Unique, because a credential id is how an assertion finds its account: two rows claiming one id would make
            // "whose passkey is this?" a question with two answers.
            // Bounded because a unique index needs a bounded column on SQL Server, and because nothing legitimate is
            // larger: 1023 bytes is WebAuthn's own ceiling for a credential id.
            b.Property(p => p.CredentialId).HasMaxLength(Passkey.CredentialIdLength).IsRequired();
            b.Property(p => p.PublicKey).HasMaxLength(Passkey.PublicKeyLength).IsRequired();

            // Named explicitly because they are internal, and EF maps only public properties by convention. Leaving
            // the counter unmapped would have read back as zero on every sign-in, which is clone detection switched
            // off without anything saying so.
            b.Property(p => p.Algorithm);
            b.Property(p => p.SignCount);
            b.HasIndex(p => p.CredentialId).IsUnique();
            b.Property(p => p.Name).HasMaxLength(Passkey.NameLength).IsRequired();
            b.Property(p => p.Transports).HasMaxLength(Passkey.TransportsLength);
        });

        // The one row that makes "the first account is the administrator" a database guarantee.
        modelBuilder.ApplyConfiguration(new AuthInstanceClaimConfiguration());

        return modelBuilder;
    }
}
