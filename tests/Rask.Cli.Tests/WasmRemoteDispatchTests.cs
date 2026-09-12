using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What <c>rask new --wasm</c> writes: a browser app in <c>Client/</c> served by the server, and remote CQRS
/// dispatch between them.
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
    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    [Fact]
    public void The_browser_app_lives_in_Client_and_the_server_writes_no_pages_of_its_own()
    {
        var files = Generate("wasm");

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
        Assert.Contains("app.UseRaskSpa();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseRask<App>", program, StringComparison.Ordinal);
        Assert.Contains("using Rask.Spa.Hosting;", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_wasm_the_server_keeps_its_pages_and_writes_no_client()
    {
        var files = Generate("cqrs");

        Assert.Contains("Features/Home/HomePage.cs", files.Keys);
        Assert.Contains("Features/Shared/App.cs", files.Keys);
        Assert.DoesNotContain(files.Keys, k => k.StartsWith("Client/", StringComparison.Ordinal));
        Assert.Contains("app.UseRask<App>();", files["Program.cs"], StringComparison.Ordinal);
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
        var client = Generate("wasm", "cqrs")["Client/Program.cs"];

        Assert.Contains("host.Services.AddRaskCqrsClient();", client, StringComparison.Ordinal);

        // The call needs the client package's OWN namespace, which is not the mediator's. Getting this wrong
        // scaffolds a project that does not compile while every assertion above still passes.
        Assert.Contains("using Rask.Cqrs.Client;", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_mediator_the_browser_app_registers_no_client()
    {
        var files = Generate("wasm");

        Assert.DoesNotContain("AddRaskCqrsClient", files["Client/Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs.Client", files["App.csproj"], StringComparison.Ordinal);

        // The endpoint half belongs to the browser rung too. Its package and using are written only with
        // --wasm, so a call written without them is an app that does not compile: the database-free
        // AddRaskCqrsServer once sat outside the wasm region, and --cqrs alone failed with CS1061.
        Assert.DoesNotContain("AddRaskCqrsServer", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs.Server", files["App.csproj"], StringComparison.Ordinal);

        // Nor its setting: an app with no endpoints has no sign-in for them to waive, and a note warning
        // that every message would answer 401 describes endpoints this app does not have.
        Assert.DoesNotContain("RequireAuthenticatedUser", files["appsettings.json"], StringComparison.Ordinal);

        // Nor the bare comment line that joined that call's paragraph to the one before it, which was left
        // dangling straight after the query cache's registration.
        Assert.DoesNotContain(
            "builder.Services.AddRaskQuery();\n//\n", files["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_client_transport_reaches_the_bundle_and_never_the_server()
    {
        var csproj = Generate("wasm", "cqrs")["App.csproj"];

        // RaskClientPackageReference is the seam: one project, two halves, one reference list.
        Assert.Contains(
            $"""<RaskClientPackageReference Include="Rask.Cqrs.Client" Version="{Version}"/>""",
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
    public void The_endpoints_are_mapped_before_the_fallback_that_would_answer_them()
    {
        var program = Generate("wasm", "cqrs")["Program.cs"];

        var map = program.IndexOf("app.MapRaskCqrs();", StringComparison.Ordinal);
        var spa = program.IndexOf("app.UseRaskSpa();", StringComparison.Ordinal);

        Assert.Contains("using Rask.Cqrs.Server;", program, StringComparison.Ordinal);
        Assert.True(map >= 0, "the CQRS endpoints are never mapped.");
        Assert.True(spa >= 0, "the browser app is never served.");
        Assert.True(map < spa, "MapRaskCqrs reads above UseRaskSpa, whose fallback answers every other route.");
    }

    [Fact]
    public void The_dashboard_is_the_one_server_rendered_part()
    {
        var program = Generate("wasm", "cqrs", "data", "ops")["Program.cs"];

        Assert.Contains("builder.Services.AddRaskServer();", program, StringComparison.Ordinal);
        Assert.Contains(
            """app.UseRaskServer<RaskDashboardShell>("/_rask/{**path}");""",
            program,
            StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("UseRaskServer<RaskDashboardShell>", StringComparison.Ordinal)
            < program.IndexOf("app.UseRaskSpa();", StringComparison.Ordinal),
            "the dashboard must be mounted above the browser app's fallback.");
    }

    [Fact]
    public void Without_a_sign_in_the_endpoints_do_not_demand_one()
    {
        // RequireAuthenticatedUser defaults to TRUE, which is right for an app that has authentication.
        // This app does not, so left on every message answers 401 — and the failure reads as broken
        // transport rather than as the secure default doing its job: a browser app that cannot reach its own
        // server. It is a setting, so it lives in appsettings.json beside the note on when to turn it back on.
        var files = Generate("wasm", "cqrs");

        Assert.Contains("builder.Services.AddRaskCqrsServer();", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("\"RequireAuthenticatedUser\": false", files["appsettings.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_database_the_secure_default_stands()
    {
        var files = Generate("wasm", "cqrs", "data");

        // A database means accounts, so there is something to authenticate — and the scaffold must not
        // hand the app a loosening it never asked for, in either file. A message reachable by anyone is a
        // decision worth making per app.
        Assert.Contains("builder.Services.AddRaskCqrsServer();", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthenticatedUser", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("RequireAuthenticatedUser", files["appsettings.json"], StringComparison.Ordinal);
    }
}
