using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Storage;

/// <summary>
/// The <see cref="StorageProvider.Database"/> provider's claim on the model: the bytes table, checked at boot like
/// <see cref="StoredFile"/> is, so a missing mapping stops the start rather than the first upload.
/// </summary>
internal sealed class StorageBytesModelCheck<TContext>(IDbContextFactory<TContext> contextFactory, StorageOptions options)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Storage";

    protected override Type Entity => typeof(StoredFileBytes);

    protected override string MapCall => "AddRaskStorage";

    protected override bool Needed => options.Provider == StorageProvider.Database;

    protected override string Alternative =>
        " Rask__Storage__Provider is Database, which keeps every file's bytes in that table; set it to Disk, S3 or Azure "
        + "to keep them elsewhere.";
}
