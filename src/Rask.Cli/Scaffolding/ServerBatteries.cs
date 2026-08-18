namespace Rask.Cli.Scaffolding;

/// <summary>
/// The opt-in batteries the <c>server</c> template can pre-wire, as chosen by <c>rask new</c>'s flags.
/// </summary>
/// <remarks>
/// A record rather than a dozen <see cref="bool"/> parameters: the generator's call sites read as
/// <c>batteries.Jobs</c> instead of a row of positional <c>true, false, false, true</c>, and a new battery
/// doesn't churn every caller and test.
/// </remarks>
internal sealed record ServerBatteries
{
    /// <summary>Cookie authentication: login + members pages and a demo credential store.</summary>
    /// <summary>
    /// Render with Rask.Bootstrap's <c>Bs*</c> components (the default). When false the template emits plain
    /// elements against a small baseline stylesheet the project owns, and no CSS-framework dependency.
    /// </summary>
    public bool Bootstrap { get; init; } = true;

    public bool Auth { get; init; }

    /// <summary>An installable PWA: manifest, icon, and offline page.</summary>
    public bool Pwa { get; init; }

    /// <summary>The source-generated CQRS mediator.</summary>
    public bool Cqrs { get; init; }

    /// <summary>A database + EF Core: an <c>AppDbContext</c> and the Rask.Data interceptors.</summary>
    public bool Data { get; init; }

    /// <summary>A Dockerfile and .dockerignore.</summary>
    public bool Docker { get; init; }

    /// <summary>Durable background jobs on the app's own database.</summary>
    public bool Jobs { get; init; }

    /// <summary>Transactional email queued on the app's own database.</summary>
    public bool Mail { get; init; }

    /// <summary>A database-backed cache (<c>ICache</c> + <c>IDistributedCache</c>).</summary>
    public bool Cache { get; init; }

    /// <summary>A transactional outbox for durable domain-event delivery.</summary>
    public bool Outbox { get; init; }

    /// <summary>Server-sent Web Push (VAPID), with subscription endpoints.</summary>
    public bool Push { get; init; }

    /// <summary>Scheduled point-in-time snapshots of the SQLite file. SQLite only.</summary>
    public bool Snapshots { get; init; }

    /// <summary>
    /// A durable log store. Alone among the batteries it does <b>not</b> imply <c>--data</c>: it keeps a
    /// SQLite file of its own rather than mapping onto the application's <c>DbContext</c>, so it needs no
    /// context, no migration, and works on an app that has no database at all.
    /// </summary>
    public bool Logs { get; init; }

    /// <summary>The operator dashboard at <c>/_ops</c> over every battery's table.</summary>
    public bool Ops { get; init; }

    /// <summary>True when any battery needs a <c>TContext</c> — i.e. a database-backed pillar is on.</summary>
    public bool AnyDbPillar => Jobs || Mail || Cache || Outbox;

    /// <summary>True when anything touches the SQLite file on disk beyond EF itself.</summary>
    /// <remarks>
    /// Continuous backup is not in here: <c>--data</c> wires Litestream on the golden path already, so a
    /// battery for it would be a second, competing registration rather than an addition.
    ///
    /// <para>
    /// <c>Logs</c> is not in here either, for a different reason: the log store owns a separate file, so it
    /// neither needs the application database nor should drag <c>--data</c> in behind it — and this
    /// property is one of the things that drives that implication in <see cref="Normalized"/>.
    /// </para>
    /// </remarks>
    public bool AnySqliteOps => Snapshots;

    /// <summary>
    /// Applies the flags' implications, so a caller can pass just what the user typed.
    /// </summary>
    /// <remarks>
    /// Each rule exists because the target simply cannot work without its dependency:
    /// <list type="bullet">
    /// <item><description>
    /// Every DB-backed pillar registers as <c>AddRaskX&lt;TContext&gt;</c> and resolves
    /// <c>IDbContextFactory&lt;TContext&gt;</c> — without <c>--data</c> there is no context to name.
    /// Snapshots likewise need a database file to copy.
    /// </description></item>
    /// <item><description>
    /// <c>--data</c> implies <c>--cqrs</c> because every scaffolded feature handler dispatches through the
    /// mediator, so one flag gives a fresh app the whole "feature → migrate" loop.
    /// </description></item>
    /// <item><description>
    /// <c>--push</c> implies <c>--pwa</c>: a browser can only subscribe to Web Push through a service
    /// worker, which is what the PWA registration installs.
    /// </description></item>
    /// </list>
    /// </remarks>
    public ServerBatteries Normalized()
    {
        // The dashboard reads AddRaskDashboard<TContext>, so it needs a context for the same reason the
        // pillars do — even on an app that has no pillars yet, where it still shows the system panel.
        var data = Data || AnyDbPillar || AnySqliteOps || Ops;
        return this with
        {
            Data = data,
            Cqrs = Cqrs || data,
            Pwa = Pwa || Push,
        };
    }
}
