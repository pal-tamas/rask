using Rask.Cli.Commands;

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
        fs.Seed("/app/Shop/Shop.csproj", SpaServerCsproj);
        fs.Seed("/app/Shop/client/package.json", """{ "name": "shop-client" }""");
        return fs;
    }

    [Fact]
    public void A_host_that_serves_a_JS_bundle_is_its_own_kind()
    {
        var target = DevTarget.Detect(Solution(), "/app/Shop", null);

        Assert.NotNull(target);
        Assert.Equal(DevTemplateKind.SpaHosted, target!.Kind);
    }

    [Fact]
    public void The_client_is_found_inside_the_host()
    {
        var target = DevTarget.Detect(Solution(), "/app/Shop", null);

        Assert.EndsWith(
            Path.Combine("Shop", "client"), target!.ClientDirectory!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_the_csproj_names_is_found_where_it_says()
    {
        var fs = new FakeFileSystem();
        fs.Seed(
            "/app/Shop/Shop.csproj",
            SpaServerCsproj.Replace(
                "<ItemGroup>",
                "<PropertyGroup><RaskSpaClientDir>app</RaskSpaClientDir></PropertyGroup><ItemGroup>",
                StringComparison.Ordinal));
        fs.Seed("/app/Shop/app/package.json", """{ "name": "shop", "scripts": { "start": "react-scripts start" } }""");

        var target = DevTarget.Detect(fs, "/app/Shop", null);

        Assert.EndsWith(Path.Combine("Shop", "app"), target!.ClientDirectory!, StringComparison.Ordinal);
        Assert.Equal("start", target.ClientDevScript);
    }

    /// <summary>
    ///     A <c>Client</c> folder holding no package.json is not a front end.
    /// </summary>
    /// <remarks>
    ///     The package.json check carries more weight under this convention than it did under the old
    ///     one. Looking for a SIBLING named <c>*.Client</c> was already narrow; looking for a FOLDER
    ///     called <c>Client</c> is not — that is an ordinary name for a project to contain, an API
    ///     client among them. What makes it a front end is the package.json, and nothing else.
    /// </remarks>
    [Fact]
    public void A_Client_folder_without_a_package_json_is_not_a_client()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/Shop/Shop.csproj", SpaServerCsproj);
        fs.Seed("/app/Shop/client/ApiClient.cs", "public class ApiClient;");

        Assert.Null(DevTarget.Detect(fs, "/app/Shop", null)!.ClientDirectory);
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
        var args = DotnetWatchInvocation.BuildDotnetArguments(
            "/app/Shop/Shop.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.SpaHosted);

        // The bundler's dev server owns the client during a dev session. Building a full production
        // bundle on every save as well would make watch unusable, and nothing would read the result.
        // Rask.Spa.Hosting.props turns RaskSpaBuild off for the dev session this names — and only a
        // project that references that package ever reads it.
        Assert.Contains($"--property:{DevCommand.DevSessionProperty}=true", args);
    }

    [Fact]
    public void Running_once_does_not_skip_the_bundle()
    {
        // --once is deliberately a plain `dotnet run` with no watching and no dev server beside it, so the
        // app has to serve a real bundle or there is nothing to look at.
        var args = DotnetWatchInvocation.BuildDotnetArguments(
            "/app/Shop/Shop.csproj", once: true, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.SpaHosted);

        Assert.DoesNotContain($"--property:{DevCommand.DevSessionProperty}=true", args);
    }

    // A dev session skips the build that would install them, so a fresh scaffold has no bundler to run yet.
    [Fact]
    public async Task A_client_with_no_node_modules_is_installed_before_its_dev_server_starts()
    {
        var fs = Solution();
        fs.Seed("/app/Shop/client/package-lock.json", "{}");
        var runner = new FakeProcessRunner();
        var command = new DevCommand(new StringConsole(), runner, fs, new FakeBrowserLauncher(), "/app/Shop");

        await command.ExecuteAsync([], CancellationToken.None);

        var npm = runner.Invocations.Where(call => call.FileName == "npm").Select(call => string.Join(' ', call.Arguments)).ToArray();
        Assert.Equal(["ci", "run dev"], npm);
    }

    [Fact]
    public async Task A_client_that_is_already_installed_goes_straight_to_its_dev_server()
    {
        var fs = Solution();
        fs.Seed("/app/Shop/client/node_modules/.package-lock.json", "{}");
        var runner = new FakeProcessRunner();
        var command = new DevCommand(new StringConsole(), runner, fs, new FakeBrowserLauncher(), "/app/Shop");

        await command.ExecuteAsync([], CancellationToken.None);

        var npm = runner.Invocations.Where(call => call.FileName == "npm").Select(call => string.Join(' ', call.Arguments)).ToArray();
        Assert.Equal(["run dev"], npm);
    }
}
