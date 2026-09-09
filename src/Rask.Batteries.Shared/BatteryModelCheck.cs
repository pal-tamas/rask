using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Rask.Batteries;

/// <summary>
///     A battery whose tables the application's <c>DbContext</c> never mapped, reported at boot rather
///     than at first use.
/// </summary>
/// <remarks>
///     <para>
///         Every battery is on by default, and a battery that is on needs its tables in the model:
///     </para>
///     <code>
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         modelBuilder.AddRaskAuth();
///         // AddRaskMail() missing — and the Mail battery is on
///     }
///     </code>
///     <para>
///         That application compiles, boots, serves pages and signs people in. It fails on the first
///         request that touches the unmapped battery, with <c>Cannot create a DbSet for 'QueuedMail'
///         because this type is not included in the model for the context</c> — which in practice means
///         the first password reset anybody asks for. The background half never complains, because a
///         worker has to tolerate a table that is not there yet: a freshly scaffolded app boots before
///         its first migration has run, and a hosted service that threw on a missing table would stop
///         the host from starting at all. So the failure surfaces in a request path, in production,
///         long after the mistake.
///     </para>
///     <para>
///         THE MODEL, NOT THE DATABASE, and that distinction is the whole reason this can fail the boot
///         where the workers cannot. <see cref="Microsoft.EntityFrameworkCore.Metadata.IModel" /> is
///         built from <c>OnModelCreating</c> and needs no connection, so "the type is not mapped" is a
///         code mistake and always wrong, while "the table does not exist yet" is normal for an app
///         that has not run <c>rask db update</c> and is not checked here at all. An app with no
///         migrations still starts.
///     </para>
///     <para>
///         Source-linked into each battery's own package rather than living in one of them, because the
///         batteries share no common assembly — <c>Rask.Cache</c> has no Rask reference at all — and
///         giving them one to carry a startup check would be the dependency creep #1014 is open about.
///         The cost of that choice is that each battery reports only itself: an app missing several
///         mappings meets them one restart at a time. Registering the check is therefore the
///         <c>AddRaskX&lt;TContext&gt;</c> overload's job, which is also what makes it fire for an app
///         that wires its batteries directly in <c>Program.cs</c> — the shape every scaffolded app has,
///         and the one a guard in the meta package misses entirely.
///     </para>
///     <para>
///         The same predicate already existed, running lazily: the operator console asks
///         <c>db.Model.FindEntityType(typeof(TEntity))</c> before drawing a queue panel, precisely so it
///         can report a battery as "not here" rather than crash on it. This runs it once, at the moment
///         the operator can still do something about it. (#1015)
///     </para>
/// </remarks>
/// <typeparam name="TContext">The application <see cref="DbContext" /> the battery was wired against.</typeparam>
internal abstract class BatteryModelCheck<TContext>(IDbContextFactory<TContext> contextFactory) : IHostedService
    where TContext : DbContext
{
    /// <summary>The battery's name, for the message.</summary>
    protected abstract string Battery { get; }

    /// <summary>An entity type the battery reads or writes, and so needs in the model.</summary>
    protected abstract Type Entity { get; }

    /// <summary>The <c>ModelBuilder</c> extension that maps it, without parentheses.</summary>
    protected abstract string MapCall { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var db = contextFactory.CreateDbContext();

        if (db.Model.FindEntityType(Entity) is not null)
        {
            return Task.CompletedTask;
        }

        // Names the LINE to type, not merely that something is wrong. The whole point of failing here
        // rather than in a request is that the person reading it is the person who can fix it.
        throw new InvalidOperationException(
            $"The {Battery} battery is on, but {Entity.Name} is not in {typeof(TContext).Name}'s model, so "
            + "the first request that uses it would fail with \"Cannot create a DbSet for "
            + $"'{Entity.Name}'\". Add it to OnModelCreating:{Environment.NewLine}"
            + $"    modelBuilder.{MapCall}();{Environment.NewLine}"
            + "Then create the migration: rask db add AddBatteryTables && rask db update. "
            + $"Turn the battery off instead by not calling {MapCall}<{typeof(TContext).Name}>().");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
