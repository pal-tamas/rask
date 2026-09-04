using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask dev</c> against a meta framework front end: two processes, and a browser pointed at the
///     framework's own dev server.
/// </summary>
/// <remarks>
///     Until this lane was taught to <c>rask dev</c>, a meta host was classified as a plain
///     <see cref="DevTemplateKind.Server" /> — which is the quiet kind of wrong. Nothing failed. The
///     front end was simply built in production mode on every save, by the framework's own toolchain,
///     and nothing in the session ever read the result.
/// </remarks>
public sealed class DevMetaHostedTests
{
    private const string MetaCsproj =
        """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <RaskMetaFramework>nuxt</RaskMetaFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Rask.Meta.Hosting" Version="1.0.0"/>
          </ItemGroup>
        </Project>
        """;

    private static FakeFileSystem Solution(string csproj = MetaCsproj, string appDir = "Client")
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/Shop/Shop.csproj", csproj);
        fs.Seed($"/app/Shop/{appDir}/package.json", """{ "scripts": { "dev": "nuxt dev" } }""");
        return fs;
    }

    [Fact]
    public void A_host_fronting_a_meta_framework_is_its_own_kind()
    {
        var target = DevTarget.Detect(Solution(), "/app/Shop", null);

        Assert.NotNull(target);
        Assert.Equal(DevTemplateKind.MetaHosted, target!.Kind);
        Assert.Equal("nuxt", target.MetaFramework);
    }

    [Fact]
    public void The_front_end_is_found_inside_the_host()
    {
        var target = DevTarget.Detect(Solution(), "/app/Shop", null);

        Assert.EndsWith(
            Path.Combine("Shop", "Client"), target!.ClientDirectory!, StringComparison.Ordinal);
        Assert.Equal("dev", target.ClientDevScript);
    }

    [Fact]
    public void A_front_end_that_was_moved_is_still_found()
    {
        // RaskMetaAppDir is documented as the way to lay the app out differently, and it is also where
        // the publish output lands. A host that used it would otherwise get no dev server, silently.
        var csproj = MetaCsproj.Replace(
            "<RaskMetaFramework>nuxt</RaskMetaFramework>",
            "<RaskMetaFramework>nuxt</RaskMetaFramework>\n    <RaskMetaAppDir>web</RaskMetaAppDir>",
            StringComparison.Ordinal);

        var target = DevTarget.Detect(Solution(csproj, "web"), "/app/Shop", null);

        Assert.EndsWith(Path.Combine("Shop", "web"), target!.ClientDirectory!, StringComparison.Ordinal);
    }

    [Theory]
    // The Nitro-based three and Next serve on 3000; the two plain Vite ones on 5173.
    [InlineData("nuxt", "http://localhost:3000")]
    [InlineData("nextjs", "http://localhost:3000")]
    [InlineData("tanstack-start", "http://localhost:3000")]
    [InlineData("solidstart", "http://localhost:3000")]
    [InlineData("sveltekit", "http://localhost:5173")]
    [InlineData("analog", "http://localhost:5173")]
    public void The_dev_server_port_follows_the_framework(string framework, string expected)
    {
        var csproj = MetaCsproj.Replace("nuxt", framework, StringComparison.Ordinal);

        var target = DevTarget.Detect(Solution(csproj), "/app/Shop", null);

        Assert.Equal(expected, target!.ClientDevServerUrl);
    }

    [Fact]
    public void A_moved_front_end_still_answers_where_its_dev_server_is()
    {
        // Derived from the framework, not from the directory, so --open does not land on Vite's port
        // because a Nuxt app was somewhere unexpected.
        var fs = new FakeFileSystem();
        fs.Seed("/app/Shop/Shop.csproj", MetaCsproj);

        var target = DevTarget.Detect(fs, "/app/Shop", null);

        Assert.Null(target!.ClientDirectory);
        Assert.Equal("http://localhost:3000", target.ClientDevServerUrl);
    }

    [Fact]
    public void A_server_host_that_merely_mentions_a_client_folder_is_not_this_lane()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""");
        fs.Seed("/app/Client/package.json", "{}");

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.Equal(DevTemplateKind.Server, target!.Kind);
        Assert.Null(target.MetaFramework);
    }

    [Fact]
    public void The_production_front_end_build_is_skipped_during_a_dev_session()
    {
        var args = DevCommand.BuildDotnetArguments(
            "/app/Shop/Shop.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.MetaHosted);

        // This is the expensive half of #983: `npm run build` here is a full PRODUCTION build of Nuxt,
        // Next or SvelteKit, on every save, whose output the session never reads.
        Assert.Contains("--property:RaskMetaBuild=false", args);
    }

    [Fact]
    public void Only_a_meta_host_skips_that_build()
    {
        var args = DevCommand.BuildDotnetArguments(
            "/app/App.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.Server);

        Assert.DoesNotContain("--property:RaskMetaBuild=false", args);
    }

    [Fact]
    public void Running_once_builds_the_front_end_for_real()
    {
        // --once is a plain `dotnet run` with no dev server beside it, so the host has to serve a real
        // build or there is nothing to look at.
        var args = DevCommand.BuildDotnetArguments(
            "/app/Shop/Shop.csproj", once: true, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.MetaHosted);

        Assert.DoesNotContain("--property:RaskMetaBuild=false", args);
    }

    [Fact]
    public void The_host_is_told_where_the_dev_server_is()
    {
        var env = DevCommand.BuildEnvironment(
            DevTemplateKind.MetaHosted, restartOnRudeEdit: true, urls: null, readEnv: _ => null,
            islandDevServerUrl: null, once: false, metaDevServerUrl: "http://localhost:3000");

        // Without this the session dies before its first page: RaskMetaBuild=false leaves no server
        // entry, and the supervisor refuses to start rather than forward into nothing.
        Assert.Equal("http://localhost:3000", env["RASK_META_DEV"]);
    }

    [Fact]
    public void Only_this_lane_is_told_that()
    {
        var env = DevCommand.BuildEnvironment(
            DevTemplateKind.SpaHosted, restartOnRudeEdit: true, urls: null, readEnv: _ => null,
            islandDevServerUrl: null, once: false, metaDevServerUrl: "http://localhost:3000");

        Assert.False(env.ContainsKey("RASK_META_DEV"));
    }

    [Fact]
    public void Running_once_leaves_the_host_supervising_its_own_front_end()
    {
        var env = DevCommand.BuildEnvironment(
            DevTemplateKind.MetaHosted, restartOnRudeEdit: false, urls: null, readEnv: _ => null,
            islandDevServerUrl: null, once: true, metaDevServerUrl: "http://localhost:3000");

        Assert.False(env.ContainsKey("RASK_META_DEV"));
    }
}
