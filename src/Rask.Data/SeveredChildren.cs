using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Deletes a child of TWO parents that was taken out of its parent's collection, rather than leaving it
///     belonging to nobody.
/// </summary>
/// <remarks>
///     A child of one parent has a required key, and EF Core deletes it when it is severed. A child of two — a chip
///     under a driver or a vehicle — holds NULL in one of its keys, so neither key can be required, and EF Core
///     severs it by setting the other one to NULL as well: a row no parent holds, which no tenant reads and nothing
///     can reach to delete. Once every parent key it had is NULL, it is deleted instead.
/// </remarks>
internal static class SeveredChildren
{
    // Whether a model has any child of several parents: a save of one that has none looks at nothing.
    private static readonly ConditionalWeakTable<IModel, StrongBox<bool>> Models = [];

    /// <summary>Marks each severed child of several parents <see cref="EntityState.Deleted" />.</summary>
    internal static void Delete(DbContext context)
    {
        if (!Models.GetValue(context.Model, static m => new(m.GetEntityTypes().Any(static t => ModelBuilderExtensions.ParentKeysOf(t) > 1))).Value)
        {
            return;
        }

        var children = context.ChangeTracker.Entries<IEntity>()
            .Where(static entry =>
                entry.State == EntityState.Modified &&
                entry.Entity is not IAggregate &&
                ModelBuilderExtensions.ParentKeysOf(entry.Metadata) > 1)
            .ToList();

        foreach (var entry in children)
        {
            var keys = entry.Metadata.GetForeignKeys()
                .Where(static key => key.PrincipalToDependent is { IsCollection: true })
                .SelectMany(static key => key.Properties)
                .Select(property => entry.Property(property.Name))
                .ToList();

            if (keys.TrueForAll(static key => key.CurrentValue is null) &&
                keys.Exists(static key => key.IsModified && key.OriginalValue is not null))
            {
                entry.State = EntityState.Deleted;
            }
        }
    }
}
