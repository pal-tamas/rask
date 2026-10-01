using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Commands;

/// <summary>Which batteries a new app gets: the flags <c>rask new</c> accepts, and the set they add up to.</summary>
internal static class BatterySelection
{
    /// <summary>Every template-scoped flag <c>rask new</c> understands: the batteries, and the test project.</summary>
    /// <remarks>
    /// <c>wasm</c> used to be here and is not a flag any more (#1103). Shipping a browser bundle changes
    /// what the app <em>is</em> rather than what it can do, so it is a TEMPLATE — <c>wasm-hosted</c>,
    /// beside <c>react</c> and the rest of the front-end-plus-host lane — and the flag that turned the
    /// server template into a different kind of app is gone. <c>server</c> is just a server.
    /// </remarks>
    internal static readonly string[] FeatureFlags =
    [
        "pwa", "cqrs", "data", "docker",
        "jobs", "mail", "cache", "storage", "outbox", "push", "snapshots", "logs", "ops", "tests",
    ];

    /// <summary>
    /// The batteries — exactly what a bare <c>rask new</c> turns on.
    /// </summary>
    /// <remarks>
    /// The same list as <see cref="FeatureFlags"/> now that <c>wasm</c> has left it, and kept as its own
    /// name because the two mean different things: one is "every flag the parser accepts", the other is
    /// "everything a bare <c>rask new</c> gives you". The default set is <c>template.SupportedFlags</c>
    /// intersected with this, so a template that cannot host a database gets the right answer without
    /// anyone maintaining a per-template default list.
    ///
    /// <para>
    /// Styling is not a decision — Tailwind is built in — and neither is authentication any more: an app
    /// with a database has accounts.
    /// </para>
    /// </remarks>
    internal static readonly string[] BatteryFlags = FeatureFlags;

    /// <summary>The <c>--no-*</c> spelling of a battery.</summary>
    internal static string OffFlag(string battery) => "no-" + battery;

    /// <summary>
    /// The batteries a project gets: everything <paramref name="template"/> supports, minus whatever
    /// <paramref name="off"/> names, with auth asked for separately.
    /// </summary>
    /// <remarks>
    /// <b>This is where "batteries included" is decided</b>, and it is decided here rather than on
    /// <see cref="ServerBatteries"/> because this is the only layer that knows the template. A default set
    /// baked into the record would have to be the same for a server app and a browser-WASM SPA, which have
    /// almost nothing in common, and would silently change what every generator test means.
    ///
    /// <para>
    /// Deriving the set from <c>template.SupportedFlags</c> means there is no per-template default list to
    /// maintain: a template that cannot host a database does not advertise <c>data</c>, so it does not get
    /// one. Adding a battery to a template's flag set is all it takes to put it on the golden path.
    /// </para>
    ///
    /// <para>
    /// <see cref="ServerBatteries.Reduced"/> runs before <see cref="ServerBatteries.Normalized"/>, and the
    /// order is load-bearing — normalizing first would turn <c>Data</c> back on for any pillar still
    /// standing and undo every <c>--no-*</c> the user typed.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A battery set with exactly the named batteries on, and nothing else.
    /// </summary>
    /// <remarks>
    /// A different question from <see cref="ToBatteries"/>, which answers "what did the user ask for on
    /// this template". This one answers "give me precisely this combination", which is what the scaffold
    /// tests, the tutorial gate and the showcase sample's provenance check need — they are pinning one
    /// shape of generated code, not the CLI's defaults, and would otherwise all change every time a
    /// template gains a battery.
    ///
    /// <para>
    /// Deliberately not normalized: a caller asking for exactly this wants exactly this, and every
    /// generator normalizes on the way in anyway.
    /// </para>
    /// </remarks>
    internal static ServerBatteries BatteriesOf(
        IReadOnlyCollection<string> on) =>
        new()
        {
            Localization = on.Contains("localization", StringComparer.Ordinal),
            Pwa = on.Contains("pwa", StringComparer.Ordinal),
            Cqrs = on.Contains("cqrs", StringComparer.Ordinal),
            Data = on.Contains("data", StringComparer.Ordinal),
            Docker = on.Contains("docker", StringComparer.Ordinal),
            Jobs = on.Contains("jobs", StringComparer.Ordinal),
            Mail = on.Contains("mail", StringComparer.Ordinal),
            Cache = on.Contains("cache", StringComparer.Ordinal),
            Storage = on.Contains("storage", StringComparer.Ordinal),
            Outbox = on.Contains("outbox", StringComparer.Ordinal),
            Push = on.Contains("push", StringComparer.Ordinal),
            Snapshots = on.Contains("snapshots", StringComparer.Ordinal),
            Logs = on.Contains("logs", StringComparer.Ordinal),
            Ops = on.Contains("ops", StringComparer.Ordinal),
            Tests = on.Contains("tests", StringComparer.Ordinal),
        };

    internal static ServerBatteries ToBatteries(
        TemplateInfo template,
        IReadOnlyCollection<string> off)
    {
        // Every battery a template supports is on unless it was turned off. There is no longer an
        // opt-in exception: localization was the only one, and it is not a flag any more (#854).
        bool On(string battery) =>
            template.SupportedFlags.Contains(battery) && !off.Contains(battery, StringComparer.Ordinal);

        return new ServerBatteries
        {
            // Not a flag any more (#854): the languages an app ships are configured in Program.cs, so
            // this is only "does this template scaffold the registration at all". CultureList stays empty
            // here — Normalized() fills in "en", the default a scaffolded app starts from and edits.
            Localization = template.ShipsLocalization,
            Pwa = On("pwa"),
            Cqrs = On("cqrs"),
            Data = On("data"),
            Docker = On("docker"),
            Jobs = On("jobs"),
            Mail = On("mail"),
            Cache = On("cache"),
            Storage = On("storage"),
            Outbox = On("outbox"),
            Push = On("push"),
            Snapshots = On("snapshots"),
            Logs = On("logs"),
            Ops = On("ops"),
            Tests = On("tests"),
        }.Reduced().Normalized();
    }

    /// <summary>Whether a resolved set includes <paramref name="battery"/>, addressed by its flag name.</summary>
    /// <remarks>
    /// The one place that maps flag names onto <see cref="ServerBatteries"/> properties, so the wizard
    /// summary and the tests read the resolved answer rather than re-deriving it from the command line.
    /// </remarks>
    internal static bool Includes(ServerBatteries batteries, string battery) => battery switch
    {
        "pwa" => batteries.Pwa,
        "cqrs" => batteries.Cqrs,
        "data" => batteries.Data,
        "docker" => batteries.Docker,
        "localization" => batteries.Localization,
        "jobs" => batteries.Jobs,
        "mail" => batteries.Mail,
        "cache" => batteries.Cache,
        "storage" => batteries.Storage,
        "outbox" => batteries.Outbox,
        "push" => batteries.Push,
        "snapshots" => batteries.Snapshots,
        "logs" => batteries.Logs,
        "ops" => batteries.Ops,
        "tests" => batteries.Tests,
        _ => false,
    };
}
