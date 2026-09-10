using Microsoft.EntityFrameworkCore;
using Rask.Auth;
using Rask.Cache;
using Rask.Data;
using Rask.Jobs;
using Rask.Mail;
using Rask.Outbox;

namespace Rask;

/// <summary>
/// The database a Rask app gets when it does not write a <see cref="DbContext" /> of its own: your
/// entities, plus every battery's tables.
/// </summary>
/// <remarks>
/// <para>
/// The entities come from <see cref="ModelRegistry" /> — everything deriving from <see cref="Model{TId}" />
/// that the source generator found, with Rask's conventions applied and each entity's own static
/// <c>Configure</c> run last. Declaring the entity is the whole of what an app
/// does: there is no context to write, no <c>DbSet</c> property to remember, and no
/// <see cref="IEntityTypeConfiguration{TEntity}" /> class.
/// </para>
/// <para>
/// <b>Every battery's tables are mapped, including the ones this app has turned off.</b> That is
/// deliberate and matches what <c>Auth</c> already promised: a battery switched off leaves its tables in
/// the model, so switching it back on is not a destructive migration, and the model does not vary between
/// two apps in one process — which is what an EF model cache keyed on the context type would get wrong.
/// </para>
/// <para>
/// An app that outgrows this writes its own context; registering an
/// <c>IDbContextFactory&lt;YourContext&gt;</c> is enough for Rask to bind everything to that one instead,
/// and this type is then never constructed. Copy the <c>OnModelCreating</c> below as the starting point.
/// </para>
/// </remarks>
public class RaskAppDbContext : RaskDbContext
{
    /// <summary>Creates the context with the options the host registered.</summary>
    public RaskAppDbContext(DbContextOptions<RaskAppDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The app's own entities first, so a battery table can never be shadowed by one of them.
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddRaskAuth();
        modelBuilder.AddRaskCache();
        modelBuilder.AddRaskJobs();
        modelBuilder.AddRaskMail();
        modelBuilder.AddRaskOutbox();
    }
}
