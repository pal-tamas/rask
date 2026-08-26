using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask dev</c> against a JS front end: two processes, and a browser pointed at the bundler.
/// </summary>
public sealed class DevSpaHostedTests
{
    private const string SpaServerCsproj =
        """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <ItemGroup>
            <PackageReference Include="Rask.Cqrs.Server" Version="1.0.0"/>
            <PackageReference Include="Rask.Spa.Hosting" Version="1.0.0"/>
          </ItemGroup>
        </Project>
        """;

    private static FakeFileSystem Solution()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/Shop.Server/Shop.Server.csproj", SpaServerCsproj);
        fs.Seed("/app/Shop.Client/package.json", """{ "name": "shop-client" }""");
        return fs;
    }

    [Fact]
    public void A_host_that_serves_a_JS_bundle_is_its_own_kind()
    {
        var target = DevTarget.Detect(Solution(), "/app/Shop.Server", null);

        Assert.NotNull(target);
        Assert.Equal(DevTemplateKind.SpaHosted, target!.Kind);
    }

    [Fact]
    public void The_client_is_found_beside_the_host()
    {
        var target = DevTarget.Detect(Solution(), "/app/Shop.Server", null);

        Assert.EndsWith("Shop.Client", target!.ClientDirectory!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sibling_that_is_not_a_JS_project_is_not_a_client()
    {
        // The exact shape this repo already contains: Rask.Cqrs.Server has a sibling Rask.Cqrs.Client,
        // and it is a C# project. Without the package.json check, `rask dev` would start a bundler in it.
        var fs = new FakeFileSystem();
        fs.Seed("/app/Shop.Server/Shop.Server.csproj", SpaServerCsproj);
        fs.Seed("/app/Shop.Client/Shop.Client.csproj", """<Project Sdk="Microsoft.NET.Sdk"></Project>""");

        Assert.Null(DevTarget.Detect(fs, "/app/Shop.Server", null)!.ClientDirectory);
    }

    [Fact]
    public void A_plain_server_project_is_unaffected()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""");

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.Equal(DevTemplateKind.Server, target!.Kind);
        Assert.Null(target.ClientDirectory);
    }

    [Fact]
    public void The_production_bundle_is_skipped_during_a_dev_session()
    {
        var args = DevCommand.BuildDotnetArguments(
            "/app/Shop.Server/Shop.Server.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.SpaHosted);

        // The bundler's dev server owns the client during a dev session. Building a full production
        // bundle on every save as well would make watch unusable, and nothing would read the result.
        Assert.Contains("--property:RaskSpaBuild=false", args);
    }

    [Fact]
    public void Only_a_SPA_host_skips_the_bundle()
    {
        var args = DevCommand.BuildDotnetArguments(
            "/app/App.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.Server);

        Assert.DoesNotContain("--property:RaskSpaBuild=false", args);
    }

    [Fact]
    public void Running_once_does_not_skip_the_bundle()
    {
        // --once is deliberately a plain `dotnet run` with no watching and no dev server beside it, so the
        // app has to serve a real bundle or there is nothing to look at.
        var args = DevCommand.BuildDotnetArguments(
            "/app/Shop.Server/Shop.Server.csproj", once: true, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.SpaHosted);

        Assert.DoesNotContain("--property:RaskSpaBuild=false", args);
    }
}
