using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
/// The battery flags' implications. Each rule exists because the target cannot work without its
/// dependency, so these are correctness rules, not conveniences.
/// </summary>
/// <remarks>
/// Driven through <c>NewCommand.ToBatteries</c> so the flag names the user actually types are part of what
/// is under test, rather than a hand-built option set that could drift from the parser.
/// </remarks>
public sealed class ServerBatteriesTests
{
    public static TheoryData<string> DbPillarFlags =>
        ["jobs", "mail", "cache", "outbox", "snapshots"];

    [Theory]
    [MemberData(nameof(DbPillarFlags))]
    public void Every_database_backed_battery_implies_data_and_cqrs(string flag)
    {
        // Each pillar registers as AddRaskX<TContext> and resolves IDbContextFactory<TContext>; without
        // --data there is no context to name. --data in turn implies --cqrs.
        var normalized = NewCommand.BatteriesOf([flag]).Normalized();

        Assert.True(normalized.Data, $"--{flag} should imply --data.");
        Assert.True(normalized.Cqrs, $"--{flag} should imply --cqrs (via --data).");
    }

    [Fact]
    public void Push_implies_pwa()
    {
        // A browser can only subscribe to Web Push through a service worker, which is what the PWA
        // registration installs.
        Assert.True(NewCommand.BatteriesOf(["push"]).Normalized().Pwa);
    }

    [Fact]
    public void Data_implies_cqrs_but_not_the_other_way_round()
    {
        Assert.True(NewCommand.BatteriesOf(["data"]).Normalized().Cqrs);
        Assert.False(NewCommand.BatteriesOf(["cqrs"]).Normalized().Data);
    }

    [Fact]
    public void A_bare_new_turns_on_every_battery_the_template_has()
    {
        var all = NewCommand.ToBatteries(TemplateCatalog.Default, []);

        Assert.True(all.Jobs);
        Assert.True(all.Mail);
        Assert.True(all.Cache);
        Assert.True(all.Outbox);
        Assert.True(all.Push);
        Assert.True(all.Snapshots);
        Assert.True(all.Logs);
        Assert.True(all.Ops);
        Assert.True(all.Data);
        Assert.True(all.Cqrs);
        Assert.True(all.Pwa);
        Assert.True(all.Docker);
        Assert.True(all.Localization);
    }

    [Fact]
    public void Ops_implies_data_because_the_dashboard_needs_a_context()
    {
        // AddRaskDashboard<TContext> has to name a context, even on an app with no pillars yet — the
        // system panel still reports how the database is configured.
        var ops = NewCommand.BatteriesOf(["ops"]).Normalized();

        Assert.True(ops.Ops);
        Assert.True(ops.Data);
        Assert.True(ops.Cqrs);
    }

    [Fact]
    public void Logs_does_not_imply_data_because_the_store_owns_its_own_file()
    {
        // The exception to the rule every other battery follows. AddRaskLogging takes a connection string
        // rather than a TContext, so an app with no database at all can still keep its log — and pulling
        // --data in behind it would scaffold an EF Core layer nobody asked for.
        var logs = NewCommand.BatteriesOf(["logs"]).Normalized();

        Assert.True(logs.Logs);
        Assert.False(logs.Data);
        Assert.False(logs.Cqrs);
        Assert.False(logs.AnyDbPillar);
        Assert.False(logs.AnySqliteOps);
    }

    [Fact]
    public void Auth_is_the_one_thing_the_defaults_leave_off()
    {
        // A login wall changes what the app IS rather than what it can do, so it stays a decision. It is
        // also the reason the wizard asks for it separately from the checklist of things to remove.
        var all = NewCommand.ToBatteries(TemplateCatalog.Default, []);

        Assert.False(all.Auth);
        Assert.True(NewCommand.ToBatteries(TemplateCatalog.Default, [], auth: true).Auth);
    }

    [Fact]
    public void A_template_only_gets_the_batteries_it_supports()
    {
        // The default set is template.SupportedFlags rather than a per-template list someone maintains, so
        // a browser-WASM SPA with no host to put a database in simply never sees one.
        _ = TemplateCatalog.TryGet("wasm", out var wasm);
        var batteries = NewCommand.ToBatteries(wasm, []);

        Assert.True(batteries.Pwa);
        Assert.True(batteries.Docker);
        Assert.False(batteries.Data);
        Assert.False(batteries.Cqrs);
        Assert.False(batteries.Ops);

        // Accepted-and-ignored for two releases: neither WASM generator reads Localization or Cultures.
        Assert.False(batteries.Localization);
    }


    public static TheoryData<string> EveryDbBattery =>
        ["jobs", "mail", "cache", "outbox", "snapshots", "ops"];

    [Theory]
    [MemberData(nameof(EveryDbBattery))]
    public void Turning_data_off_takes_every_database_backed_battery_with_it(string battery)
    {
        // The mirror of the implication above: each of these registers as AddRaskX<TContext>, so leaving
        // them on without a context would scaffold a registration naming something that isn't there.
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, ["data"]);

        Assert.False(batteries.Data);
        Assert.False(NewCommand.Includes(batteries, battery));

        // …and the log store is untouched, because it owns a database of its own.
        Assert.True(batteries.Logs);
    }

    [Fact]
    public void Turning_cqrs_off_takes_the_database_with_it()
    {
        // Every scaffolded feature handler dispatches through the mediator, so a context with no mediator
        // has nothing to reach it.
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, ["cqrs"]);

        Assert.False(batteries.Cqrs);
        Assert.False(batteries.Data);
        Assert.False(batteries.Jobs);
        Assert.False(batteries.Ops);
    }

    [Fact]
    public void Turning_the_pwa_off_takes_web_push_with_it()
    {
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, ["pwa"]);

        Assert.False(batteries.Pwa);
        Assert.False(batteries.Push);
    }

    [Fact]
    public void Turning_push_off_leaves_the_pwa_standing()
    {
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, ["push"]);

        Assert.False(batteries.Push);
        Assert.True(batteries.Pwa);
    }

    [Fact]
    public void Turning_one_battery_off_leaves_the_rest_alone()
    {
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, ["jobs"]);

        Assert.False(batteries.Jobs);
        Assert.True(batteries.Data);
        Assert.True(batteries.Mail);
        Assert.True(batteries.Cache);
        Assert.True(batteries.Outbox);
        Assert.True(batteries.Ops);
    }

    [Fact]
    public void Turning_localization_off_clears_the_language_list()
    {
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, ["localization"]);

        Assert.False(batteries.Localization);
        Assert.Empty(batteries.Cultures);
    }

    [Fact]
    public void Reducing_is_idempotent()
    {
        var once = NewCommand.ToBatteries(TemplateCatalog.Default, ["data"]);

        Assert.Equal(once, once.Reduced());
        Assert.Equal(once, once.Reduced().Normalized());
    }

    [Fact]
    public void Every_battery_the_wizard_offers_has_a_description()
    {
        // The checklist reads from BatteryDescriptions rather than the schema, whose text is written for
        // the --no- spelling. A battery added without a line here would render a blank row.
        foreach (var battery in NewCommand.BatteryFlags)
        {
            Assert.True(
                NewCommand.BatteryDescriptions.ContainsKey(battery),
                $"--{battery} has no wizard description.");
        }
    }

    [Fact]
    public void An_empty_set_stays_empty()
    {
        var normalized = NewCommand.BatteriesOf([]).Normalized();

        Assert.False(normalized.Data);
        Assert.False(normalized.Cqrs);
        Assert.False(normalized.Pwa);
        Assert.False(normalized.AnyDbPillar);
        Assert.False(normalized.AnySqliteOps);
    }

    [Fact]
    public void Normalizing_twice_changes_nothing()
    {
        // GenerateServer normalizes on entry; a caller that already normalized must get the same result.
        var once = NewCommand.BatteriesOf(["jobs", "push"]).Normalized();

        Assert.Equal(once, once.Normalized());
    }

    [Fact]
    public void Every_battery_flag_is_supported_by_the_server_template()
    {
        // A flag the schema accepts but the template rejects would be a confusing hard error.
        var server = Templates.TemplateCatalog.All.Single(t => t.Key == "server");

        foreach (var flag in NewCommand.FeatureFlags)
        {
            Assert.Contains(flag, server.SupportedFlags);
        }
    }
}
