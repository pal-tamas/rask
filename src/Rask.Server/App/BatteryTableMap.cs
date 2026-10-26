using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask;

/// <summary>Maps one battery's tables when it is on — and, when it is off, keeps the ones an existing database has.</summary>
/// <remarks>
/// <para>
/// A battery that is off has no tables in a new app, the way a Laravel app has no jobs table until it asks for one.
/// </para>
/// <para>
/// An app whose migrations already created them keeps them, excluded from migrations: leaving them out would make the
/// next <c>rask db add</c> drop the table, and its rows with it, for flipping one switch. Whether the migrations
/// created them is read off the app's own model snapshot, which is what that next migration is diffed against.
/// </para>
/// </remarks>
internal static class BatteryTableMap
{
    internal static void Map(
        ModelBuilder modelBuilder, bool on, Func<ModelBuilder, ModelBuilder> map, Func<IReadOnlyModel?> snapshot)
    {
        if (on)
        {
            map(modelBuilder);
            return;
        }

        // No migrations, no table to keep: the battery is simply not mapped.
        if (snapshot() is not { } existing)
        {
            return;
        }

        var before = modelBuilder.Model.GetEntityTypes().ToHashSet();
        map(modelBuilder);
        var added = modelBuilder.Model.GetEntityTypes().Where(e => !before.Contains(e) && !e.IsOwned()).ToList();

        if (added.Exists(e => existing.FindEntityType(e.Name) is not null))
        {
            added.ForEach(static e => e.SetIsTableExcludedFromMigrations(excluded: true));
            return;
        }

        foreach (var entityType in added)
        {
            modelBuilder.Ignore(entityType.ClrType);
        }
    }
}
