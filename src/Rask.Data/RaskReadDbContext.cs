using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The context the generated read faces are queried through. It holds no aggregate, so there is nothing
///     in it to track, change or save.
/// </summary>
/// <remarks>
///     <para>
///         The two halves are separate types on purpose. An aggregate sees another only by id, so a write
///         cannot cross a boundary by accident; a read face carries the navigations the write model is not
///         allowed to have and may join across as many aggregates as it likes. Keeping them in one context
///         would also be illegal — two entity types cannot map one table unless they are a table-splitting
///         pair — and would put <c>db.Add(orderRead)</c> back within reach.
///     </para>
///     <para>
///         Its model is <see cref="ReadModelRegistry" />, mirrored from the write model: the read face of an
///         entity lands on the same table, the same columns and the same conversions EF built for the entity
///         itself, rather than on a second derivation of them that could drift.
///     </para>
/// </remarks>
public class RaskReadDbContext : DbContext, ITenantScoped
{
    /// <summary>Creates the context with the options the host registered.</summary>
    public RaskReadDbContext(DbContextOptions<RaskReadDbContext> options)
        : base(options)
    {
    }

    /// <summary>Creates the context for a derived type supplying its own options.</summary>
    protected RaskReadDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        ReadModelRegistry.Apply(modelBuilder, WriteModel(), this);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // The read faces keep an entity's strongly-typed ids as they are — Order.Read.Where(o => o.CustomerId
        // == customer.Id) reads the same on both sides — so they need the same converters registered.
        ModelRegistry.ApplyConventions(configurationBuilder);
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        base.OnConfiguring(optionsBuilder);

        // Untracked at the CONTEXT, not only at the head of each query. ModelQuery already starts every read
        // with AsNoTracking() — it has to, because the same type also queries aggregates through the write
        // context — but this context holds no aggregate at all, so there is nothing here worth watching for
        // changes under any circumstances. Saying it once, structurally, means the escape hatches inherit it:
        // a QueryAsync callback, an AsQueryable() handed to a grid, and anything an app writes against a read
        // context of its own are all untracked without having to remember.
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        // Deliberate, not an oversight EF is warning about: a read face's principal carries the soft-delete
        // query filter, so a row whose customer was deleted drops out of the join instead of failing. That is
        // what the read side is for — showing what is there now.
        optionsBuilder.ConfigureWarnings(static w => w.Ignore(
            CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

        // EF Core caches a context's model by its TYPE, and this one's model is mirrored from whichever
        // write context was ambient when it was first built. One cache entry would therefore pin the first
        // write model the process ever saw — right in an app with one database, and wrong the moment there
        // are two, or a test suite that points Db somewhere new per class. Keying on Db's generation
        // rebuilds the mirror exactly when the write side moved, and never otherwise.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ReadModelCacheKeyFactory>();
    }

    // Opened once, while the read model is built, and disposed immediately. Building a DbContext's model is
    // cached per context type, so this is one extra context for the life of the process — and it is the only
    // way to see what the write side's own Configure decided.
    private static IModel? WriteModel()
    {
        if (!Db.IsConfigured)
        {
            return null;
        }

        using var write = Db.CreateContext();
        return write.Model;
    }

    private sealed class ReadModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            ArgumentNullException.ThrowIfNull(context);
            return (context.GetType(), designTime, Db.Generation);
        }
    }
}
