using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>An app on several domains: <c>--domain ppta.hu --domain www.ppta.hu</c>, the first the primary.</summary>
public sealed partial class DeployCommandTests
{
    [Fact]
    public void Several_domains_join_in_the_order_given_whether_repeated_or_comma_separated()
    {
        string[] given = ["PPTA.hu", "www.ppta.hu, api.ppta.hu"];

        var parsed = SiteDomains.TryParse(given, out var site, out var error);

        Assert.True(parsed, error);
        Assert.Equal("ppta.hu,www.ppta.hu,api.ppta.hu", site);
        Assert.Equal("ppta.hu", SiteDomains.Primary(site!));
    }

    [Fact]
    public void A_domain_given_twice_is_refused_rather_than_dropped()
    {
        string[] given = ["ppta.hu", "PPTA.HU"];

        var parsed = SiteDomains.TryParse(given, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_host_of_a_list_goes_through_the_domain_check()
    {
        // The list is split before the check, so one good host cannot carry a hostile one past it.
        string[] given = ["ppta.hu,evil.hu {\n}\n:80 {"];

        var parsed = SiteDomains.TryParse(given, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("isn't a valid domain", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_on_several_domains_is_one_caddy_site_with_every_address()
    {
        var routes = new SortedDictionary<string, RouteTarget>(StringComparer.Ordinal)
        {
            ["ppta.hu,www.ppta.hu"] = new RouteTarget("ta-blue", 8080),
            ["shop.example.com"] = new RouteTarget("shop-blue", 8080),
        };

        var caddyfile = CaddyRouting.BuildCaddyfile(routes);

        Assert.Equal(
            "ppta.hu, www.ppta.hu {\n\tencode zstd gzip\n\treverse_proxy ta-blue:8080\n}\n\nshop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-blue:8080\n}\n",
            caddyfile);
    }

    [Fact]
    public void The_domain_label_of_an_app_on_several_domains_reads_back_whole()
    {
        var listing = "ta-blue\tta\tppta.hu,www.ppta.hu\tblue\t8080\n";

        var apps = CaddyRouting.ParseDeployedApps(listing);

        Assert.Equal("ppta.hu,www.ppta.hu", Assert.Single(apps).Domain);
    }

    [Fact]
    public void A_host_another_app_already_serves_is_found_with_that_app()
    {
        IReadOnlyList<DeployedApp> apps =
        [
            new("shop-blue", "shop", "shop.example.com,www.example.com", "blue", 8080),
            new("ta-blue", "ta", "ppta.hu", "blue", 8080),
        ];

        var taken = CaddyRouting.FindTakenHost(apps, "ta", "ppta.hu,WWW.example.com");

        Assert.Equal(("www.example.com", "shop", false), taken);
    }

    [Fact]
    public void An_apps_own_domains_never_count_as_taken()
    {
        IReadOnlyList<DeployedApp> apps = [new("ta-blue", "ta", "ppta.hu", "blue", 8080)];

        var taken = CaddyRouting.FindTakenHost(apps, "ta", "ppta.hu,www.ppta.hu");

        Assert.Null(taken);
    }

    [Fact]
    public void One_domain_is_remembered_as_domain_and_several_as_domains()
    {
        var one = new DeployConfig();
        var several = new DeployConfig();

        one.SetSite("ppta.hu");
        several.SetSite("ppta.hu,www.ppta.hu");

        Assert.Equal(("ppta.hu", (string[]?)null), (one.Domain, one.Domains));
        Assert.Null(several.Domain);
        Assert.Equal(["ppta.hu", "www.ppta.hu"], several.Domains!);
    }

    [Fact]
    public void A_deploy_json_with_domains_reads_back_as_the_same_site()
    {
        var fs = new FakeFileSystem();
        fs.Seed(DeployConfig.PathFor(WorkingDir), """{"host":"deploy@box","domains":["ppta.hu","www.ppta.hu"]}""");

        var config = DeployConfig.Load(fs, WorkingDir);

        Assert.Equal("ppta.hu,www.ppta.hu", config.Site);
    }

    [Fact]
    public void A_deploy_json_written_before_domains_still_reads_its_one_domain()
    {
        var fs = new FakeFileSystem();
        fs.Seed(DeployConfig.PathFor(WorkingDir), """{"host":"deploy@box","domain":"app.example.com"}""");

        var config = DeployConfig.Load(fs, WorkingDir);

        Assert.Equal("app.example.com", config.Site);
    }

    [Fact]
    public async Task Deploying_on_several_domains_labels_routes_and_remembers_them_all()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box", "--app", "ta", "--domain", "ppta.hu", "--domain", "www.ppta.hu"], CancellationToken.None);

        Assert.Equal(0, exit);
        var run = runner.Invocations.Single(i => i.Arguments.Contains("rask.app=ta"));
        Assert.Contains("rask.domain=ppta.hu,www.ppta.hu", run.Arguments);
        Assert.Contains("Rask__Auth__PublicOrigin=https://ppta.hu", run.Arguments); // the primary
        Assert.Contains("ppta.hu, www.ppta.hu {", TheCaddyfile(fs), StringComparison.Ordinal);
        Assert.Equal(["ppta.hu", "www.ppta.hu"], DeployConfig.Load(fs, WorkingDir).Domains!);
        Assert.Contains("https://ppta.hu, https://www.ppta.hu", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redeploy_with_no_domain_flag_keeps_every_remembered_domain()
    {
        var fs = new FakeFileSystem();
        new DeployConfig { Host = "deploy@box", Name = "ta", Domains = ["ppta.hu", "www.ppta.hu"] }.Save(fs, WorkingDir);
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("rask.domain=ppta.hu,www.ppta.hu"));
    }

    [Fact]
    public async Task A_domain_another_app_serves_stops_the_deploy_before_anything_starts()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-blue\tshop\twww.ppta.hu\tblue\t8080\n") };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box", "--app", "ta", "--domain", "ppta.hu,www.ppta.hu"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("www.ppta.hu is already served by the app 'shop'", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("rask.app=ta"));
        Assert.DoesNotContain(fs.Written.Keys, path => path.EndsWith(".caddy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_domain_does_not_apply_to_an_action_on_what_is_deployed()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--domain", "a.example.com", "--domain", "b.example.com"], CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.Contains("--domain doesn't apply to `rask deploy status`", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }
}
