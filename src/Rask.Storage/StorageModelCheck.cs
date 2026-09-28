using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Storage;

/// <summary>Storage's claim on the application's model, checked once at boot (see <c>BatteryModelCheck</c>).</summary>
internal sealed class StorageModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Storage";

    protected override Type Entity => typeof(StoredFile);

    protected override string MapCall => "AddRaskStorage";
}
