using Rask.Cli.Commands;

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
        var normalized = NewCommand.ToBatteries([flag]).Normalized();

        Assert.True(normalized.Data, $"--{flag} should imply --data.");
        Assert.True(normalized.Cqrs, $"--{flag} should imply --cqrs (via --data).");
    }

    [Fact]
    public void Push_implies_pwa()
    {
        // A browser can only subscribe to Web Push through a service worker, which is what the PWA
        // registration installs.
        Assert.True(NewCommand.ToBatteries(["push"]).Normalized().Pwa);
    }

    [Fact]
    public void Data_implies_cqrs_but_not_the_other_way_round()
    {
        Assert.True(NewCommand.ToBatteries(["data"]).Normalized().Cqrs);
        Assert.False(NewCommand.ToBatteries(["cqrs"]).Normalized().Data);
    }

    [Fact]
    public void All_batteries_turns_on_every_pillar()
    {
        var all = NewCommand.ToBatteries(["all-batteries"]).Normalized();

        Assert.True(all.Jobs);
        Assert.True(all.Mail);
        Assert.True(all.Cache);
        Assert.True(all.Outbox);
        Assert.True(all.Push);
        Assert.True(all.Snapshots);
        Assert.True(all.Data);
        Assert.True(all.Cqrs);
        Assert.True(all.Pwa);
    }

    [Fact]
    public void All_batteries_does_not_imply_auth_or_docker()
    {
        // Those are deployment/product choices, not batteries — `--all-batteries` shouldn't decide them.
        var all = NewCommand.ToBatteries(["all-batteries"]).Normalized();

        Assert.False(all.Auth);
        Assert.False(all.Docker);
    }

    [Fact]
    public void An_empty_set_stays_empty()
    {
        var normalized = NewCommand.ToBatteries([]).Normalized();

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
        var once = NewCommand.ToBatteries(["jobs", "push"]).Normalized();

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
