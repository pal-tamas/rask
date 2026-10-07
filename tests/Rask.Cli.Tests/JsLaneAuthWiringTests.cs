using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The accounts endpoints are reachable from a scaffolded front end — from the host that maps them, and
///     through the dev server the browser actually talks to. Either half missing fails the same silent way:
///     a front end that compiles, runs, and cannot sign anyone in.
/// </summary>
public sealed class JsLaneAuthWiringTests
{
    private const string Root = "/proj/App";

    public static TheoryData<string> Frameworks() => [.. SpaFramework.All.Select(framework => framework.Key)];

    private static ScaffoldResult Generate(string key, bool data) =>
        ProjectGenerator.GenerateSpa(
            Root, "App", SpaFramework.All.Single(f => f.Key == key), new ServerBatteries { Data = data }, "1.2.3");

    // Serve() maps /api/auth whenever the auth battery is on; what the scaffold owes is not switching it off.
    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_spa_template_is_served_with_its_accounts_on(string key)
    {
        var program = Program(Generate(key, data: true));

        Assert.Contains("app.Serve();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Auth.Off()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Data.Off()", program, StringComparison.Ordinal);
    }

    // Accounts are rows, so turning the database off takes them with it.
    [Fact]
    public void An_app_with_no_database_maps_nothing_it_cannot_answer()
    {
        var program = Program(Generate("react", data: false));

        Assert.Contains("c.Data.Off();", program, StringComparison.Ordinal);
    }

    // In a dev session a path the bundler does not forward is a 404 from the bundler, with the host running beside it.
    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_spa_dev_server_forwards_the_auth_endpoints(string key)
    {
        var result = Generate(key, data: true);

        // Angular declares its proxy in proxy.conf.json; the Vite frameworks in vite.config.ts.
        var proxies = result.Files
            .Where(f => Path.GetFileName(f.Path) is "vite.config.ts" or "proxy.conf.json")
            .Select(f => f.Content)
            .Where(c => c.Contains("/_rask", StringComparison.Ordinal))
            .ToArray();

        Assert.True(proxies.Length > 0, $"[{key}] no dev proxy was scaffolded at all.");
        Assert.Contains(proxies, c => c.Contains("/api/auth", StringComparison.Ordinal));
    }

    private static string Program(ScaffoldResult result) =>
        result.Files
            .Single(f => f.Path.Replace('\\', '/').EndsWith("/Program.cs", StringComparison.Ordinal))
            .Content;
}
