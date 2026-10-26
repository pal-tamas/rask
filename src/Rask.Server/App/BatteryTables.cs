namespace Rask;

/// <summary>The database-backed batteries whose tables <see cref="RaskAppDbContext" /> maps: the ones that are on.</summary>
/// <remarks>The outbox is not here: it follows the database, so its table is always mapped.</remarks>
[Flags]
internal enum BatteryTables
{
    None = 0,
    Auth = 1,
    Cache = 2,
    Jobs = 4,
    Mail = 8,
    Storage = 16,
    Push = 32,
    Logs = 64,
    All = Auth | Cache | Jobs | Mail | Storage | Push | Logs,
}
