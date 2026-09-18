using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     The application's <see cref="DbContext" />, so the application does not have to write one.
/// </summary>
/// <remarks>
///     <para>
///         Its model is <see cref="ModelRegistry" /> — every <see cref="Aggregate{TId}" /> the source generator
///         found, with Rask's conventions applied and each entity's own static <c>Configure</c> run
///         last. Declaring an entity is the whole of what an app does; there is no context class, no
///         <c>DbSet</c> property, and no <see cref="IEntityTypeConfiguration{TEntity}" /> to write.
///     </para>
///     <para>
///         The model surface (<c>Product.Where(…)</c>, <c>Product.FindAsync(id)</c>) reaches it with
///         nothing injected — a Rask session outlives any context, so what is registered is an
///         <see cref="IDbContextFactory{TContext}" /> and each call opens its own. The writes on the type
///         (<c>Product.CreateAsync</c>, <c>UpdateAsync</c>, <c>DeleteAsync</c>) do the same, or join a context
///         they are handed; anything richer injects that factory (or, in a scoped handler, the context) and uses
///         EF Core directly.
///     </para>
///     <para>
///         <b>An app that outgrows this writes its own context and Rask steps aside.</b> Registering an
///         <c>IDbContextFactory&lt;YourContext&gt;</c> is enough — the batteries bind the model surface to
///         the context the app registered, and this one is never used. Call
///         <c>modelBuilder.ApplyRaskConventions()</c> (or <c>ModelRegistry.Apply(modelBuilder)</c> to keep
///         the generated model too) from its <c>OnModelCreating</c>.
///     </para>
/// </remarks>
public class RaskDbContext : DbContext, ITenantScoped
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
    /// <remarks>
    ///     An explicit <see cref="Tenant.Use" /> or <see cref="Tenant.Across" /> wins; otherwise the tenant
    ///     is the one on the signed-in principal, which is what makes a page filter correctly without
    ///     anything being passed to it.
    /// </remarks>
    public Guid? CurrentTenant => Tenant.Resolve();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        ModelRegistry.Apply(modelBuilder, this);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Silences EF Core's <c>NoEntityTypeConfigurationsWarning</c> (event 10632): "No
    ///         instantiatable types implementing <c>IEntityTypeConfiguration</c> were found while scanning
    ///         assembly '…'".
    ///     </para>
    ///     <para>
    ///         In an EF app that warning is a typo-catcher — you called
    ///         <c>ApplyConfigurationsFromAssembly</c> and pointed it at the wrong assembly. In a Rask app it
    ///         is the <b>normal state</b>: the whole point of deriving from <c>Aggregate&lt;TId&gt;</c> is
    ///         that there is no DbSet, no configuration class and no registration to write, so an app can
    ///         map its entire model and still own not one <c>IEntityTypeConfiguration</c>. The scaffold
    ///         keeps the <c>ApplyConfigurationsFromAssembly</c> call — so that adding a configuration class
    ///         later Just Works — and it therefore warned on the first <c>rask db update</c> of every new
    ///         project, about a file the user was never told to write.
    ///     </para>
    ///     <para>
    ///         Suppressed here rather than in the template, so it is true of every Rask context however its
    ///         options were built, and <c>Ignore</c> rather than the app's own
    ///         <c>ConfigureWarnings</c> so an app that genuinely wants the check back can still
    ///         <c>Throw</c> or <c>Log</c> it — a later call wins.
    ///     </para>
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(CoreEventId.NoEntityTypeConfigurationsWarning));
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
