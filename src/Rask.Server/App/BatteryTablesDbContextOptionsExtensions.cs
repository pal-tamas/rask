using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask;

/// <summary>Tells <see cref="RaskAppDbContext" /> which batteries are on.</summary>
internal static class BatteryTablesDbContextOptionsExtensions
{
    internal static DbContextOptionsBuilder WithBatteryTables(this DbContextOptionsBuilder builder, BatteryTables tables)
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new BatteryTablesExtension(tables));
        return builder;
    }

    /// <summary>The battery tables an app with <paramref name="options" /> uses, on <paramref name="provider" />.</summary>
    /// <remarks>The log keeps a file of its own on SQLite, so only a server database maps its table.</remarks>
    internal static BatteryTables BatteryTablesFor(RaskAppOptions options, RaskDatabaseProvider provider)
    {
        var tables = BatteryTables.None;
        tables |= options.Auth.Enabled ? BatteryTables.Auth : BatteryTables.None;
        tables |= options.Cache.Enabled ? BatteryTables.Cache : BatteryTables.None;
        tables |= options.Jobs.Enabled ? BatteryTables.Jobs : BatteryTables.None;
        tables |= options.Mail.Enabled ? BatteryTables.Mail : BatteryTables.None;
        tables |= options.Storage.Enabled ? BatteryTables.Storage : BatteryTables.None;
        tables |= options.Push.Enabled ? BatteryTables.Push : BatteryTables.None;
        tables |= options.Logs.Enabled && provider != RaskDatabaseProvider.Sqlite ? BatteryTables.Logs : BatteryTables.None;
        return tables;
    }
}
