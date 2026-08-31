using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What <c>rask new --wasm</c> writes to make remote CQRS dispatch work across the one-project build.
/// </summary>
/// <remarks>
/// <para>
/// The one-project build compiles a single set of sources into two halves, so the interesting assertions
/// here are about what each half gets and what it must NOT get. <c>Rask.Cqrs.Client</c> in the server
/// would ship endpoint-calling code into the process that answers those endpoints, which is the whole
/// reason those two packages were split.
/// </para>
/// <para>
/// These pin generated text. Whether any of it restores and compiles is a different question, and only a
/// real publish can answer it — see <see cref="BrowserRungPublishE2ETests"/>.
/// </para>
/// </remarks>
public sealed class WasmRemoteDispatchTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    // Flags in, files out — the same path `rask new` takes, so the flag names are under test too.
    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    [Fact]
    public void The_browser_half_is_given_somewhere_to_register_its_client()
    {
        var files = Generate("wasm", "cqrs");

        // The bundle has no Program.cs of its own — that file is the server's, and the companion excludes
        // it — so without this there is nowhere for AddRaskCqrsClient to be called at all.
        Assert.True(
            files.ContainsKey("Browser/BrowserStartup.cs"),
            "a --wasm --cqrs app has no BrowserStartup, so its bundle can never register the client.");

        Assert.Contains(
            "services.AddRaskCqrsClient();",
            files["Browser/BrowserStartup.cs"],
            StringComparison.Ordinal);

        // The call needs the client package's OWN namespace, which is not the mediator's. Getting this
        // wrong scaffolds a project that does not compile, and every assertion above still passes — the
        // publish gate is what caught it the first time.
        Assert.Contains(
            "using Rask.Cqrs.Client;",
            files["Browser/BrowserStartup.cs"],
            StringComparison.Ordinal);

        // And the csproj has to name it, or the generated entry point never calls it.
        Assert.Contains(
            "<RaskBrowserStartup>$(RootNamespace).Browser.BrowserStartup</RaskBrowserStartup>",
            files["App.csproj"],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasm")] // the browser rung, but no mediator to dispatch
    [InlineData("cqrs")] // a mediator, but no browser half to dispatch from
    public void Nothing_browser_only_is_written_without_both_halves(string flag)
    {
        var files = Generate(flag);

        Assert.False(
            files.ContainsKey("Browser/BrowserStartup.cs"),
            $"--{flag} alone scaffolded a browser startup it has no use for.");

        Assert.DoesNotContain("Rask.Cqrs.Client", files["App.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_client_transport_reaches_the_bundle_and_never_the_server()
    {
        var csproj = Generate("wasm", "cqrs")["App.csproj"];

        // RaskBrowserPackageReference is the seam: one project, two halves, one reference list. As a
        // plain PackageReference this would compile endpoint-CALLING code into the process that answers
        // those endpoints.
        Assert.Contains(
            $"""<RaskBrowserPackageReference Include="Rask.Cqrs.Client" Version="{Version}"/>""",
            csproj,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            """<PackageReference Include="Rask.Cqrs.Client" """,
            csproj,
            StringComparison.Ordinal);

        // The endpoint half is an ordinary reference, because the server is what answers.
        Assert.Contains(
            $"""<PackageReference Include="Rask.Cqrs.Server" Version="{Version}"/>""",
            csproj,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoints_are_mapped_before_the_catch_all_that_would_swallow_them()
    {
        var program = Generate("wasm", "cqrs")["Program.cs"];

        // UseRask ends the pipeline with a catch-all that renders a page for any unmatched path. Mapped
        // after it, /_rask/cqrs/request/{name} is answered with HTML rather than reached — which surfaces
        // in the browser as a JSON parse error, a long way from the line that caused it.
        var routing = program.IndexOf("app.UseRouting();", StringComparison.Ordinal);
        var map = program.IndexOf("app.MapRaskCqrs();", StringComparison.Ordinal);
        var rask = program.IndexOf("app.UseRask<App>();", StringComparison.Ordinal);

        // Same trap as the client's: the endpoint half has its own namespace, and without the using the
        // whole scaffolded app fails to compile rather than mis-ordering anything.
        Assert.Contains("using Rask.Cqrs.Server;", program, StringComparison.Ordinal);

        Assert.True(routing >= 0, "UseRouting is never called, so the endpoints have no router.");
        Assert.True(map >= 0, "the CQRS endpoints are never mapped.");
        Assert.True(rask >= 0, "the Rask catch-all is never mounted.");

        Assert.True(routing < map, "MapRaskCqrs needs a router, so UseRouting must come first.");
        Assert.True(map < rask, "MapRaskCqrs must precede UseRask or the catch-all answers the endpoints.");
    }

    [Fact]
    public void Without_a_sign_in_the_endpoints_do_not_demand_one()
    {
        // RequireAuthenticatedUser defaults to TRUE, which is right for an app that has authentication.
        // This app does not, so left on every message answers 401 — and the failure reads as broken
        // transport rather than as the secure default doing its job. That is the exact shape this feature
        // exists to avoid: a page that looks eligible for the browser and cannot reach its own server.
        Assert.Contains(
            "builder.Services.AddRaskCqrsServer(o => o.RequireAuthenticatedUser = false);",
            Generate("wasm", "cqrs")["Program.cs"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_sign_in_the_secure_default_stands()
    {
        var program = Generate("wasm", "cqrs", "auth")["Program.cs"];

        // There is something to authenticate now, so the scaffold must not hand the app a loosening it
        // never asked for — a message reachable by anyone is a decision worth making per app.
        Assert.Contains("builder.Services.AddRaskCqrsServer();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthenticatedUser", program, StringComparison.Ordinal);
    }
}
