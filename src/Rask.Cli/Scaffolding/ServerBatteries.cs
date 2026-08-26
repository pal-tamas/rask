namespace Rask.Cli.Scaffolding;

/// <summary>
/// The batteries a template can pre-wire, as resolved from <c>rask new</c>'s flags.
/// </summary>
/// <remarks>
/// A record rather than a dozen <see cref="bool"/> parameters: the generator's call sites read as
/// <c>batteries.Jobs</c> instead of a row of positional <c>true, false, false, true</c>, and a new battery
/// doesn't churn every caller and test.
///
/// <para>
/// <b>Every property here defaults to <c>false</c>, and that is not what <c>rask new</c> defaults to.</b>
/// The command turns on every battery the chosen template supports — see
/// <c>NewCommand.ToBatteries</c>, which owns that decision because it is the only place that knows the
/// template. This type stays the neutral carrier, so <c>new ServerBatteries()</c> keeps meaning
/// "explicitly nothing" for the generators, their tests, and callers that want a deliberately lean
/// project.
/// </para>
/// </remarks>
internal sealed record ServerBatteries
{
    /// <summary>Translate the UI: per-language catalogs, a negotiated culture, and a switcher.</summary>
    public bool Localization { get; init; }

    /// <summary>
    /// The languages to scaffold a catalog for, comma-joined and ordered — the first is the default that
    /// negotiation falls back to.
    /// </summary>
    /// <remarks>
    /// A <b>string</b> rather than a list, and that is not a style choice. This type is a record, so a
    /// collection property would silently degrade its synthesized value equality to reference equality:
    /// two batteries describing the same languages would compare unequal, and the tests that compare
    /// battery values would start failing in some later, unrelated change. <see cref="Cultures"/> is the
    /// readable view.
    /// </remarks>
    public string CultureList { get; init; } = "";

    /// <summary>The configured languages, in order.</summary>
    public IEnumerable<string> Cultures =>
        CultureList.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Cookie authentication: login + members pages and a demo credential store.</summary>
    /// <summary>
    ///     How the generated pages are styled.
    /// </summary>
    /// <remarks>
    ///     <see cref="Scaffolding.Styling.Plain" /> is the default: a small stylesheet the project owns,
    ///     and no CSS-framework dependency at all. It is the one answer that assumes nothing about what
    ///     you are building — Bootstrap and Tailwind are both opinions, and neither should be the one you
    ///     get by not choosing.
    /// </remarks>
    public Styling Styling { get; init; } = Styling.Plain;

    /// <summary>
    ///     Whether the pages use Rask.Bootstrap's <c>Bs*</c> components.
    /// </summary>
    /// <remarks>
    ///     Derived rather than stored, so the generators that ask "Bs* or plain elements?" keep reading
    ///     the way they did while there is exactly one place that decides it.
    /// </remarks>
    public bool Bootstrap => Styling == Styling.Bootstrap;

    /// <summary>Whether the build compiles a Tailwind stylesheet for this project.</summary>
    public bool Tailwind => Styling == Styling.Tailwind;

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

    /// <summary>The operator dashboard at <c>/_rask</c> over every battery's table.</summary>
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

        // Naming a language is asking for localization; asking for localization with no language named
        // means English, which is what an app gets before it adds a second one.
        var localization = Localization || CultureList.Length > 0;

        return this with
        {
            Data = data,
            Cqrs = Cqrs || data,
            Pwa = Pwa || Push,
            Localization = localization,
            CultureList = localization && CultureList.Length == 0 ? "en" : CultureList,
        };
    }

    /// <summary>
    /// Applies the implications <em>downwards</em> — turning a battery off takes with it everything that
    /// cannot work without it.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="Normalized"/>, and it exists because the batteries are on by default:
    /// <c>--no-data</c> has to mean "and therefore no jobs, mail, cache, outbox, snapshots or dashboard",
    /// or the command would scaffold registrations naming a <c>DbContext</c> that isn't there.
    ///
    /// <para>
    /// <b>Order is load-bearing.</b> Reduce first, then normalize: <see cref="Normalized"/> turns
    /// <c>Data</c> back on for any pillar still standing, so running it first would undo every
    /// <c>--no-*</c> the user typed. <c>Logs</c> is untouched — it owns a file of its own and never
    /// depended on <c>Data</c> in either direction.
    /// </para>
    /// </remarks>
    public ServerBatteries Reduced()
    {
        // Every pillar registers as AddRaskX<TContext>, so losing the context loses all of them. And
        // losing the mediator loses the context, because every scaffolded feature dispatches through it.
        var data = Data && Cqrs;

        return this with
        {
            Data = data,
            Jobs = Jobs && data,
            Mail = Mail && data,
            Cache = Cache && data,
            Outbox = Outbox && data,
            Snapshots = Snapshots && data,
            Ops = Ops && data,

            // A browser subscribes to Web Push through the service worker the PWA registration installs.
            Push = Push && Pwa,
            CultureList = Localization ? CultureList : "",
        };
    }
}

/// <summary>
///     How a scaffolded project styles its pages.
/// </summary>
/// <remarks>
///     One axis with three answers rather than a pair of booleans. A <c>--bootstrap --tailwind</c>
///     pair would have had to mean something, and every combination of two flags that are really one
///     choice ends up with a state nobody designed.
/// </remarks>
internal enum Styling
{
    /// <summary>Plain elements and a small stylesheet the project owns. The default.</summary>
    Plain,

    /// <summary>Rask.Bootstrap's <c>Bs*</c> components over Bootstrap 5.3.</summary>
    Bootstrap,

    /// <summary>Tailwind utilities, compiled from the project's own source by Rask.Tailwind.</summary>
    Tailwind,
}
