using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// Names the proxy redirects to an app (<c>--redirect '*.eszaf.hu'</c>), and the DNS provider that proves a wildcard's
/// certificate (<c>--dns azure</c>): Let's Encrypt issues a wildcard only over DNS-01.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string AzureTls =
        "\ttls {\n\t\tdns azure {\n"
        + "\t\t\ttenant_id {env.AZURE_TENANT_ID}\n\t\t\tclient_id {env.AZURE_CLIENT_ID}\n\t\t\tclient_secret {env.AZURE_CLIENT_SECRET}\n"
        + "\t\t\tsubscription_id {env.AZURE_SUBSCRIPTION_ID}\n\t\t\tresource_group_name {env.AZURE_RESOURCE_GROUP_NAME}\n"
        + "\t\t}\n\t}\n";

    private const string AzureCredentials =
        "AZURE_TENANT_ID=t\nAZURE_CLIENT_ID=c\nAZURE_CLIENT_SECRET=top-secret-value\nAZURE_SUBSCRIPTION_ID=s\nAZURE_RESOURCE_GROUP_NAME=rg\n";

    private static SortedDictionary<string, RouteTarget> EszafRoutes() => new(StringComparer.Ordinal)
    {
        ["eszaf.hu,www.eszaf.hu"] = new RouteTarget("eszaf-blue", 8080, "*.eszaf.hu"),
        ["ppta.hu"] = new RouteTarget("ta-green", 8080),
    };

    [Fact]
    public void A_redirect_sends_every_name_on_to_the_primary_with_its_path_and_method()
    {
        var routes = EszafRoutes();

        var caddyfile = CaddyRouting.BuildCaddyfile(routes, DnsProvider.Azure);

        // 308: a program still POSTing to an old name arrives with its method and body.
        Assert.Contains("*.eszaf.hu {\n" + AzureTls + "\tredir https://eszaf.hu{uri} 308\n}\n", caddyfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_served_names_are_their_own_block_ahead_of_the_wildcard_that_only_redirects()
    {
        // Caddy matches the most specific address, so eszaf.hu and www.eszaf.hu stay the app's while every other
        // subdomain redirects; the served blocks come first so the file reads app by app.
        var routes = EszafRoutes();

        var caddyfile = CaddyRouting.BuildCaddyfile(routes, DnsProvider.Azure);

        Assert.Equal(
            "eszaf.hu, www.eszaf.hu {\n\treverse_proxy eszaf-blue:8080\n}\n\n"
            + "ppta.hu {\n\treverse_proxy ta-green:8080\n}\n\n"
            + "*.eszaf.hu {\n" + AzureTls + "\tredir https://eszaf.hu{uri} 308\n}\n",
            caddyfile);
    }

    [Fact]
    public void Only_a_block_naming_a_wildcard_proves_its_certificate_over_dns()
    {
        var routes = new SortedDictionary<string, RouteTarget>(StringComparer.Ordinal)
        {
            ["*.shop.example.com,shop.example.com"] = new RouteTarget("shop-blue", 8080),
            ["ppta.hu"] = new RouteTarget("ta-green", 8080),
        };

        var caddyfile = CaddyRouting.BuildCaddyfile(routes, DnsProvider.Azure);

        Assert.Equal(
            "*.shop.example.com, shop.example.com {\n" + AzureTls + "\treverse_proxy shop-blue:8080\n}\n\n"
            + "ppta.hu {\n\treverse_proxy ta-green:8080\n}\n",
            caddyfile);
    }

    [Fact]
    public void A_box_with_no_redirect_and_no_wildcard_writes_the_file_it_always_did()
    {
        var routes = new SortedDictionary<string, RouteTarget>(StringComparer.Ordinal)
        {
            ["ppta.hu,www.ppta.hu"] = new RouteTarget("ta-blue", 8080),
            ["shop.example.com"] = new RouteTarget("shop-blue", 8080),
        };

        var withProvider = CaddyRouting.BuildCaddyfile(routes, DnsProvider.Azure);

        Assert.Equal(CaddyRouting.BuildCaddyfile(routes), withProvider);
        Assert.DoesNotContain("tls", withProvider, StringComparison.Ordinal);
    }

    [Fact]
    public void The_listing_reads_redirects_from_their_label_and_an_older_row_as_having_none()
    {
        const string listing = "eszaf-blue\teszaf\teszaf.hu,www.eszaf.hu\tblue\t8080\t*.eszaf.hu\nta-green\tta\tppta.hu\tgreen\t8080\n";

        var apps = CaddyRouting.ParseDeployedApps(listing);

        Assert.Equal("*.eszaf.hu", apps[0].Redirects);
        Assert.Equal(string.Empty, apps[1].Redirects);
    }

    [Fact]
    public void A_name_another_app_redirects_is_taken_like_one_it_serves()
    {
        IReadOnlyList<DeployedApp> apps = [new DeployedApp("eszaf-blue", "eszaf", "eszaf.hu", "blue", 8080, "*.eszaf.hu")];

        var taken = CaddyRouting.FindTakenHost(apps, "other", "other.example.com", "*.eszaf.hu");

        Assert.Equal(("*.eszaf.hu", "eszaf", true), taken);
    }

    [Fact]
    public void Redirects_are_remembered_and_a_file_without_them_reads_and_writes_as_before()
    {
        var fs = new FakeFileSystem();
        var config = new DeployConfig { Host = "deploy@box", Domain = "eszaf.hu" };
        config.SetRedirects("*.eszaf.hu");
        config.Save(fs, WorkingDir);
        var none = new DeployConfig { Host = "deploy@box", Domain = "ppta.hu" };
        none.SetRedirects(string.Empty);

        var loaded = DeployConfig.Load(fs, WorkingDir);
        none.Save(fs, "/other");

        Assert.Equal(["*.eszaf.hu"], loaded.Redirects!);
        Assert.Equal("*.eszaf.hu", loaded.RedirectSite);
        Assert.DoesNotContain("redirects", fs.Files["/other/.rask/deploy.json"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_proxy_with_a_dns_provider_is_its_image_labelled_with_the_credentials_from_their_file()
    {
        var stock = DockerCommands.BuildCaddyRunArguments("deploy@box", "rask");

        var withDns = DockerCommands.BuildCaddyRunArguments("deploy@box", "rask", DnsProvider.Azure, "/proj/azure-dns.env");

        Assert.Equal("caddy:2", stock[^1]);
        Assert.DoesNotContain("rask.dns=azure", stock);
        Assert.Equal("rask-caddy:dns-azure", withDns[^1]);
        Assert.Contains("rask.dns=azure", withDns);
        Assert.Contains("rask-caddy-data:/data", withDns);
        Assert.Equal("/proj/azure-dns.env", withDns[withDns.ToList().IndexOf("--env-file") + 1]);
    }

    [Fact]
    public void The_dns_image_is_caddy_rebuilt_with_the_providers_module()
    {
        var dockerfile = DnsProvider.Azure.Dockerfile;

        Assert.Contains("FROM caddy:2-builder AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN xcaddy build --with github.com/caddy-dns/azure", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=build /usr/bin/caddy /usr/bin/caddy", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wildcard_redirect_with_no_dns_provider_stops_the_deploy_before_anything_starts()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = ProxyCaptures(dns: "") };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box", "--app", "eszaf", "--domain", "eszaf.hu", "--redirect", "*.eszaf.hu"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("no DNS provider", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("--dns azure", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("rask.app=eszaf"));
    }

    [Fact]
    public async Task A_deploy_with_no_dns_flag_keeps_the_proxys_provider_and_never_recreates_it()
    {
        // CI's deploy: no --dns, no credentials. The proxy says it was built with azure, and is left as it is.
        var fs = new FakeFileSystem();
        new DeployConfig { Host = "deploy@box", Name = "eszaf", Domains = ["eszaf.hu", "www.eszaf.hu"], Redirects = ["*.eszaf.hu"] }.Save(fs, WorkingDir);
        var runner = new FakeProcessRunner { CaptureHandler = ProxyCaptures(dns: "azure") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments is [.., "rm", "-f", "rask-caddy"]);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("rask-caddy:dns-azure"));
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("rask.redirect=*.eszaf.hu"));
        Assert.Contains("*.eszaf.hu {\n" + AzureTls, TheCaddyfile(fs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asking_for_a_dns_provider_replaces_the_stock_proxy_only_once_the_new_app_answers()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/azure-dns.env", AzureCredentials);
        var runner = new FakeProcessRunner { CaptureHandler = ProxyCaptures(dns: "", dnsImageBuilt: false) };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box", "--app", "eszaf", "--domain", "eszaf.hu,www.eszaf.hu", "--redirect", "*.eszaf.hu", "--dns", "azure", "--dns-env-file", "azure-dns.env"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        var probe = runs.FindIndex(i => i.Arguments.Contains("curlimages/curl:8.23.0"));
        var build = runs.FindIndex(i => i.Arguments.Contains("build") && i.Arguments.Contains("rask-caddy:dns-azure"));
        var remove = runs.FindIndex(i => i.Arguments is [.., "rm", "-f", "rask-caddy"]);
        var start = runs.FindIndex(i => i.Arguments.Contains("rask.dns=azure"));
        Assert.True(probe >= 0 && probe < build && build < remove && remove < start, "built, then replaced, after the new app answered");
        Assert.Equal("/proj/azure-dns.env", runs[start].Arguments[runs[start].Arguments.ToList().IndexOf("--env-file") + 1]);
        Assert.Contains("every site on this box is down", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-value", console.OutText + console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("dns", fs.Files["/proj/.rask/deploy.json"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_credentials_file_missing_a_variable_is_refused_by_name_without_its_values()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/azure-dns.env", "AZURE_CLIENT_SECRET=top-secret-value\n");
        var runner = new FakeProcessRunner { CaptureHandler = ProxyCaptures(dns: "") };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box", "--app", "eszaf", "--domain", "eszaf.hu", "--dns", "azure", "--dns-env-file", "azure-dns.env"], CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.Contains("AZURE_TENANT_ID", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-value", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task A_dry_run_names_the_credentials_file_by_placeholder_and_never_its_values()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/azure-dns.env", AzureCredentials);
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box", "--app", "eszaf", "--domain", "eszaf.hu", "--redirect", "*.eszaf.hu", "--dns", "azure", "--dns-env-file", "azure-dns.env", "--dry-run"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("--env-file <dns-env-file> rask-caddy:dns-azure", console.OutText, StringComparison.Ordinal);
        Assert.Contains("rask.redirect=*.eszaf.hu", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-value", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redirect_needs_a_domain_to_send_its_names_on_to()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--app", "eszaf", "--redirect", "*.eszaf.hu"], CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.Contains("--redirect needs a --domain", console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deploy flow against a proxy built with <paramref name="dns"/> ("" = the stock image), and a DNS image that
    /// is or isn't on the box yet.
    /// </summary>
    private static Func<IReadOnlyList<string>, ProcessResult> ProxyCaptures(string dns, bool dnsImageBuilt = true) => args =>
        args.Contains("index .Config.Labels \"rask.dns\"}}") || args.Any(arg => arg.Contains("rask.dns", StringComparison.Ordinal) && arg.Contains("index", StringComparison.Ordinal))
            ? new ProcessResult(0, dns + "\n", string.Empty)
        : args.Contains("image") && args.Contains("rask-caddy:dns-azure") ? new ProcessResult(dnsImageBuilt ? 0 : 1, string.Empty, string.Empty)
        : Captures()(args);
}
