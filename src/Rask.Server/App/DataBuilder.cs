using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server;

/// <summary>
///     What <c>AddRaskData&lt;TContext&gt;(…)</c> hands back: the steps that say more about
///     <typeparamref name="TContext" /> than where its data is.
/// </summary>
/// <typeparam name="TContext">The context <c>AddRaskData</c> registered.</typeparam>
/// <remarks>
///     <code>
/// builder.Services.AddRaskData&lt;TaDomain&gt;(o =&gt; o.UseSqlServer(connection))
///     .OwnsSchema(s =&gt; s.TakesOverFrom("WallClockMoments").WhenTable("Tenant").Patience(20.Minutes));
///     </code>
/// </remarks>
public sealed class DataBuilder<TContext>
    where TContext : DbContext
{
    internal DataBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection <c>AddRaskData</c> registered into, to go on registering with.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Makes <typeparamref name="TContext" />'s migrations the owner of the database: the host applies the pending
    ///     ones as it starts, before anything else does — one host at a time.
    /// </summary>
    /// <param name="configure">
    ///     Takes over a schema that predates the migrations, and gives a slow migration the time it needs —
    ///     <c>s =&gt; s.TakesOverFrom("WallClockMoments").WhenTable("Tenant").Patience(20.Minutes)</c>.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Without it a hand-wired host creates, migrates and checks nothing, which is what an app whose schema is
    ///         owned elsewhere needs. With it the host does what a <c>RaskApp</c> does: in its <c>StartingAsync</c>,
    ///         ahead of every hosted service and of the web server, it applies the migrations EF Core reports pending,
    ///         and a model changed since its last migration stops the start.
    ///     </para>
    ///     <para>
    ///         Two hosts starting together — a blue and a green — take EF Core's migration lock in turn
    ///         (<c>sp_getapplock</c> on SQL Server, a lock table on SQLite), so a schema is taken over, and each
    ///         migration applied, once.
    ///     </para>
    /// </remarks>
    public DataBuilder<TContext> OwnsSchema(Action<SchemaOwnership>? configure = null)
    {
        var ownership = new SchemaOwnership();
        configure?.Invoke(ownership);

        Services.AddSingleton(new OwnedSchema<TContext>(ownership));
        Services.AddHostedService<MigrateOnStart<TContext>>();
        return this;
    }
}
