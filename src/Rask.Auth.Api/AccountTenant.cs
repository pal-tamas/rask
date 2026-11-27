using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Data;

namespace Rask.Auth;

/// <summary>
/// The tenant an account belongs to: the app's own <c>TenantId</c> column, in the type <c>IBelongsTo&lt;Tenant&gt;</c>
/// gives it — the tenant's typed id, its number, or a <see cref="Guid" /> — and optional, because an administrator
/// belongs to none.
/// </summary>
/// <remarks>
/// <para>
/// The column is the app's, exactly as on any table that belongs to the tenant: <c>User</c> declares
/// <c>public TenantId? TenantId { get; private set; }</c> to read and set it, or leaves it to a shadow column. Unlike
/// such a table the accounts are neither filtered nor stamped by the tenant in flight — sign-in finds an account before
/// it knows its tenant, and an administrator's row has none — so Rask.Auth maps the column itself, and
/// <c>IBelongsTo&lt;Tenant&gt;</c> on an account says only its type and the foreign key to the tenant.
/// </para>
/// <para>
/// Beside it Rask.Auth keeps <c>TenantKey</c>, the same tenant as the <see cref="Guid" /> the framework carries with
/// <see cref="Guid.Empty" /> for none, which is what the unique index on the sign-in name is over and what the principal
/// is built from.
/// </para>
/// </remarks>
internal static class AccountTenant
{
    /// <summary>Maps the account's <c>TenantId</c> in the type it keeps its tenant in, and the <c>TenantKey</c> beside it.</summary>
    /// <typeparam name="TUser">The account type.</typeparam>
    /// <typeparam name="TKey">Its key.</typeparam>
    /// <param name="b">The account's builder.</param>
    /// <exception cref="InvalidOperationException">It declares a <c>TenantId</c> no tenant is kept in, or not its tenant's key.</exception>
    internal static void Map<TUser, TKey>(EntityTypeBuilder<TUser> b)
        where TUser : Authenticatable<TKey>
        where TKey : notnull
    {
        b.Property(TenantColumn.TypeFor(b.Metadata), Columns.TenantId);
        b.Property(u => u.TenantKey).HasColumnName(Columns.TenantKey);
    }

    /// <summary>
    /// Puts a new account in the tenant the work is in — an administrator inviting someone into the tenant they act
    /// in — unless the app already gave it one.
    /// </summary>
    /// <param name="db">The context <paramref name="user" /> was added to.</param>
    /// <param name="user">The new account.</param>
    internal static void JoinInFlight(DbContext db, object user)
    {
        if (Tenant.Key is not { } tenant || tenant == Tenant.Nobody)
        {
            return;
        }

        var entry = db.Entry(user);
        var column = entry.Property(Columns.TenantId);
        if (column.CurrentValue is null)
        {
            column.CurrentValue = TenantColumn.ValueFor(tenant, column.Metadata.ClrType, TableOf(entry.Metadata));
        }
    }

    private static string TableOf(IReadOnlyEntityType entityType) => entityType.GetTableName() ?? entityType.ClrType.Name;
}
