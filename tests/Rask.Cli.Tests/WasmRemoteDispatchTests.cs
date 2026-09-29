using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What <c>rask new --template wasm-hosted</c> writes: a browser app in <c>Client/</c> served by the host,
/// and remote CQRS dispatch between them.
/// </summary>
/// <remarks>
/// <para>
/// One project, two halves. The interesting assertions are about what each half gets and what it must NOT
/// get: <c>Rask.Cqrs.Client</c> in the server would ship endpoint-calling code into the process that
/// answers those endpoints, which is the whole reason those two packages were split.
/// </para>
/// <para>
/// These pin generated text. Whether any of it restores and compiles is a different question, and only a
/// real publish can answer it — see <c>ClientPublishE2ETests</c>.
/// </para>
/// </remarks>
public sealed class WasmRemoteDispatchTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    // Flags in, files out — the same path `rask new` takes, so the flag names are under test too.
    private static Dictionary<string, string> Index(ScaffoldResult result) =>
        result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content,
            StringComparer.Ordinal);

    /// <summary>The wasm-hosted template. CQRS is not passed: the generator forces it on.</summary>
    private static Dictionary<string, string> Hosted(params string[] flags) =>
        Index(ProjectGenerator.GenerateWasmHosted(Root, "App", NewCommand.BatteriesOf(flags), Version));

    /// <summary>The server template, for the assertions about what it must NOT carry.</summary>
    private static Dictionary<string, string> Server(params string[] flags) =>
        Index(ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version));

    [Fact]
    public void The_browser_app_lives_in_Client_and_the_server_writes_no_pages_of_its_own()
    {
        var files = Hosted();

        Assert.Contains("Client/Program.cs", files.Keys);
        Assert.Contains("Client/App.cs", files.Keys);
        Assert.Contains("Client/Pages/HomePage.cs", files.Keys);
        Assert.Contains("Client/wwwroot/index.html", files.Keys);

        // The server's own pages are REPLACED, not joined: two App classes and two routes for "/" in one
        // project would be a compile error on one half and a routing collision on the other.
        Assert.DoesNotContain("Features/Home/HomePage.cs", files.Keys);
        Assert.DoesNotContain("Features/Shared/App.cs", files.Keys);
        Assert.DoesNotContain("Features/Shared/ErrorPage.cs", files.Keys);

        var program = files["Program.cs"];
        Assert.Contains("app.Serve();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Run<App>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void With_every_battery_on_the_server_is_one_line()
    {
        var files = Hosted([.. NewCommand.BatteryFlags]);

        var program = files["Program.cs"];

        // The committed file, untouched: nothing turned off means nothing for Program.cs to say.
        Assert.Contains("RaskApp.Create(args).Serve();", program, StringComparison.Ordinal);
        Assert.DoesNotContain(".Off();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_server_template_keeps_its_pages_and_writes_no_client()
    {
        var files = Server("cqrs");

        Assert.Contains("Features/Home/HomePage.cs", files.Keys);
        Assert.Contains("Features/Shared/App.cs", files.Keys);
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("Client/", StringComparison.Ordinal));
        Assert.Contains(".Run<App>();", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs.Client", files["App.csproj"], StringComparison.Ordinal);

        // Nor the endpoint half of remote dispatch, which answers a browser app this project does not have.
        // Its package and using are written only with --wasm, so a call written without them is an app that
        // does not compile: the database-free AddRaskCqrsServer once sat outside the wasm region, and --cqrs
        // alone failed with CS1061.
        Assert.DoesNotContain("AddRaskCqrsServer", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs.Server", files["App.csproj"], StringComparison.Ordinal);

        // Nor its setting: an app with no endpoints has no sign-in for them to waive.
        Assert.DoesNotContain("RequireAuthenticatedUser", files["appsettings.json"], StringComparison.Ordinal);

        // Nor the bare comment line that joined that call's paragraph to the one before it, which was left
        // dangling straight after the query cache's registration.
        Assert.DoesNotContain(
            "builder.Services.AddRaskQuery();\n//\n", files["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_browser_app_registers_its_client_in_its_own_entry_point()
    {
        var client = Hosted()["Client/Program.cs"];

        Assert.Contains("host.Services.AddRaskCqrsClient();", client, StringComparison.Ordinal);

        // The call needs the client package's OWN namespace, which is not the mediator's. Getting this wrong
        // scaffolds a project that does not compile while every assertion above still passes.
        Assert.Contains("using Rask.Cqrs.Client;", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_no_mediator_still_wires_both_halves()
    {
        // There is no such thing as a wasm-hosted app without the mediator: the wire between the halves IS
        // the template, so the generator forces CQRS back on and NewCommand refuses --no-cqrs outright
        // (NewCommandTests holds the refusal). This pins the generator's half — a template that quietly
        // honoured the flag would scaffold a browser app with no way to reach its own server.
        var files = Hosted([.. NewCommand.BatteryFlags.Where(f => f != "cqrs")]);

        Assert.Contains("AddRaskCqrsClient", files["Client/Program.cs"], StringComparison.Ordinal);
        Assert.Contains("Rask.Cqrs.Client", files["App.csproj"], StringComparison.Ordinal);
        Assert.Contains(".Serve();", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("c.Cqrs.Off()", files["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_client_transport_reaches_the_bundle_and_never_the_server()
    {
        var csproj = Hosted()["App.csproj"];

        // RaskClientPackageReference is the seam: one project, two halves, one reference list.
        Assert.Contains(
            $"""<RaskClientPackageReference Include="Rask.Cqrs.Client" Version="{Version}"/>""",
            csproj,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            """<PackageReference Include="Rask.Cqrs.Client" """,
            csproj,
            StringComparison.Ordinal);

        // The endpoint half arrives with the host package, because the server is what answers.
        Assert.Contains(
            $"""<PackageReference Include="Rask.Server" Version="{Version}"/>""",
            csproj,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs.Server", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void A_battery_left_out_is_a_line_in_Program_cs()
    {
        // Where the endpoints and the console are mapped is RaskApp.Serve's business now (RaskAppServeTests);
        // what the scaffold still decides is which batteries this app does without.
        var program = Hosted("data")["Program.cs"];

        Assert.Contains("c.Ops.Off();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("c.Data.Off();", program, StringComparison.Ordinal);
        Assert.Contains("app.Serve();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pwa_is_the_browser_apps_so_the_server_never_switches_it()
    {
        // Serve wires no server-side PWA — Client/Program.cs's UsePwa is the app's — so an app without one
        // says so by leaving push out, which is the part the server does run.
        var program = Hosted("data")["Program.cs"];

        Assert.DoesNotContain("c.Pwa.Off();", program, StringComparison.Ordinal);
        Assert.Contains("c.Push.Off();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_sign_in_the_endpoints_do_not_demand_one()
    {
        // RequireAuthenticatedUser defaults to TRUE, which is right for an app that has authentication.
        // This app does not, so left on every message answers 401 — and the failure reads as broken
        // transport rather than as the secure default doing its job: a browser app that cannot reach its own
        // server. It is a setting, so it lives in appsettings.json beside the note on when to turn it back on.
        var files = Hosted();

        Assert.Contains("c.Data.Off();", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("\"RequireAuthenticatedUser\": false", files["appsettings.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_database_the_secure_default_stands()
    {
        var files = Hosted("data");

        // A database means accounts, so there is something to authenticate — and the scaffold must not
        // hand the app a loosening it never asked for, in either file. A message reachable by anyone is a
        // decision worth making per app.
        Assert.DoesNotContain("c.Data.Off();", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthenticatedUser", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthenticatedUser", files["appsettings.json"], StringComparison.Ordinal);
    }
}
