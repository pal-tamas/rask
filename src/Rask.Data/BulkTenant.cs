namespace Rask.Data;

/// <summary>
///     The tenant column of a per-tenant table, as the bulk writer stamps and checks it in <see cref="AuditingInterceptor" />'s
///     place: a row without a tenant takes the one in flight, a row with one must have that one (or be written inside
///     <see cref="Tenant.Across" />).
/// </summary>
internal sealed class BulkTenant(Func<object, object?> read, Action<object, object?> write, Type column, string table)
{
    internal void Stamp(object entity)
    {
        var current = read(entity);
        if (current is null || Equals(current, Guid.Empty))
        {
            write(entity, TenantColumn.InFlight(column, table));
            return;
        }

        if (Tenant.IsAcrossTenants)
        {
            return; // set deliberately, on somebody's behalf
        }

        var inFlight = TenantColumn.ValueFor(Tenant.RequiredKey, column, table);
        if (!Equals(current, inFlight))
        {
            throw new InvalidOperationException(
                $"'{table}' is bulk inserted with tenant {current} in tenant {inFlight}. A row belongs to the tenant it is " +
                "written in; insert it there, or inside Tenant.Across() when crossing tenants is the point.");
        }
    }
}
