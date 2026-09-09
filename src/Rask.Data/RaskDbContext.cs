using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The application's <see cref="DbContext" />, so the application does not have to write one.
/// </summary>
/// <remarks>
///     <para>
///         Its model is <see cref="ModelRegistry" /> — every <see cref="Model" /> the source generator
///         found, with Rask's conventions applied and each entity's own static <c>Configure</c> run
///         last. Declaring an entity is the whole of what an app does; there is no context class, no
///         <c>DbSet</c> property, and no <see cref="IEntityTypeConfiguration{TEntity}" /> to write.
///     </para>
///     <para>
///         It is reached through the active-record surface (<c>Product.Where(…)</c>, <c>Db.Current</c>),
///         not by injection — a Rask session outlives any context, so what is registered is an
///         <see cref="IDbContextFactory{TContext}" /> and each unit of work opens its own.
///     </para>
///     <para>
///         <b>An app that outgrows this writes its own context and Rask steps aside.</b> Registering an
///         <c>IDbContextFactory&lt;YourContext&gt;</c> is enough — the batteries bind the ambient
///         database to the context the app registered, and this one is never used. Call
///         <c>modelBuilder.ApplyRaskConventions()</c> (or <c>ModelRegistry.Apply(modelBuilder)</c> to keep
///         the generated model too) from its <c>OnModelCreating</c>.
///     </para>
/// </remarks>
public class RaskDbContext : DbContext
{
    /// <summary>Creates the context with the options the host registered.</summary>
    public RaskDbContext(DbContextOptions<RaskDbContext> options)
        : base(options)
    {
    }

    /// <summary>Creates the context for a derived type supplying its own options.</summary>
    protected RaskDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        ModelRegistry.Apply(modelBuilder);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Where the strongly-typed ids get their value converters. It has to be here rather than in
    ///     <see cref="OnModelCreating" />: EF Core reads conventions first, and a converter registered
    ///     after the model is built never reaches the key it was written for.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);
        ModelRegistry.ApplyConventions(configurationBuilder);
    }
}
