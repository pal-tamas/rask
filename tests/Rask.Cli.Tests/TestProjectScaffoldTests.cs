using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

// `rask new Shop` puts a Shop.Tests project beside the app with one passing test, so a new app starts with
// a green `dotnet test`. `--no-tests` leaves it out.
public sealed class TestProjectScaffoldTests
{
    private const string Root = "/proj/Shop";
    private const string Version = "9.9.9";

    [Fact]
    public void A_new_server_app_has_a_test_project_that_references_it()
    {
        var files = Server(off: []);

        var csproj = files["Shop.Tests/Shop.Tests.csproj"];
        Assert.Contains("""<ProjectReference Include="..\Shop.csproj"/>""", csproj, StringComparison.Ordinal);
        Assert.Contains($"""<PackageReference Include="Rask.Testing" Version="{Version}"/>""", csproj, StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="Microsoft.NET.Test.Sdk" """, csproj, StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="xunit.v3" """, csproj, StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="xunit.runner.visualstudio" """, csproj, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void The_test_project_is_an_xunit_v3_executable_that_dotnet_test_runs_through_vstest(string template)
    {
        var files = template == "wasm" ? Wasm(off: []) : Server(off: []);

        var csproj = files["Shop.Tests/Shop.Tests.csproj"];

        Assert.Contains("<OutputType>Exe</OutputType>", csproj, StringComparison.Ordinal);
        Assert.Contains("<IsTestingPlatformApplication>false</IsTestingPlatformApplication>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("""<PackageReference Include="xunit" """, csproj, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void The_first_test_asserts_the_greeting_the_home_page_renders(string template)
    {
        var files = template == "wasm" ? Wasm(off: []) : Server(off: []);

        var test = files["Shop.Tests/Features/Home/HomePageTests.cs"];
        Assert.Contains("namespace Shop.Tests.Features.Home;", test, StringComparison.Ordinal);
        Assert.Contains("public void Home_page_greets_the_visitor()", test, StringComparison.Ordinal);
        Assert.Contains("Page.Render(() => HomePage)", test, StringComparison.Ordinal);
        Assert.Contains("Assert.Contains(\"Hello, Rask!\", page.Html);", test, StringComparison.Ordinal);
        Assert.Contains("[\"Hello, Rask!", files["Features/Home/HomePage.cs"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void The_solution_lists_the_test_project_beside_the_app(string template)
    {
        var files = template == "wasm" ? Wasm(off: []) : Server(off: []);

        var slnx = files["Shop.slnx"];
        Assert.Contains("""<Project Path="Shop.csproj" />""", slnx, StringComparison.Ordinal);
        Assert.Contains("""<Project Path="Shop.Tests/Shop.Tests.csproj" />""", slnx, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void The_app_keeps_the_test_folder_out_of_its_own_build(string template)
    {
        var files = template == "wasm" ? Wasm(off: []) : Server(off: []);

        var csproj = files["Shop.csproj"];
        Assert.Contains(
            "<DefaultItemExcludes>$(DefaultItemExcludes);Shop.Tests/**</DefaultItemExcludes>", csproj, StringComparison.Ordinal);
        Assert.Contains(
            @"<RaskScopedSourceExclude>$(RaskScopedSourceExclude);Shop.Tests\**</RaskScopedSourceExclude>", csproj, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void No_tests_leaves_the_project_its_solution_entry_and_the_exclusion_out(string template)
    {
        var files = template == "wasm" ? Wasm(off: ["tests"]) : Server(off: ["tests"]);

        Assert.DoesNotContain(files.Keys, path => path.StartsWith("Shop.Tests/", StringComparison.Ordinal));
        Assert.DoesNotContain("Shop.Tests", files["Shop.slnx"], StringComparison.Ordinal);
        Assert.DoesNotContain("Shop.Tests", files["Shop.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_wasm_test_project_compiles_the_app_sources_without_its_browser_entry_point()
    {
        var files = Wasm(off: []);

        var csproj = files["Shop.Tests/Shop.Tests.csproj"];
        Assert.Contains(@"<Compile Include=""..\**\*.cs""", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Shop.Tests\**", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Program.cs", csproj, StringComparison.Ordinal);
        Assert.Contains("<RootNamespace>Shop</RootNamespace>", csproj, StringComparison.Ordinal);
        Assert.Contains($"""<PackageReference Include="Rask.Wasm" Version="{Version}"/>""", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_test_packages_are_pinned_to_the_versions_this_repo_tests_with()
    {
        var pins = RepoPins.Packages();

        var projects = new[] { Server(off: []), Wasm(off: []) }.Select(files => files["Shop.Tests/Shop.Tests.csproj"]);

        foreach (var csproj in projects)
        {
            foreach (var package in new[] { "Microsoft.NET.Test.Sdk", "xunit.v3", "xunit.runner.visualstudio" })
            {
                Assert.Contains($"<PackageReference Include=\"{package}\" Version=\"{pins[package]}\"", csproj, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_next_steps_mention_dotnet_test_only_when_there_are_tests()
    {
        var batteries = NewCommand.ToBatteries(TemplateCatalog.Default, []);

        var with = ProjectGenerator.GenerateServer(Root, "Shop", batteries, Version);
        var without = ProjectGenerator.GenerateServer(Root, "Shop", batteries with { Tests = false }, Version);

        Assert.Contains("dotnet test", with.Notes, StringComparison.Ordinal);
        Assert.Contains("Rask.Testing", with.Packages);
        Assert.DoesNotContain("dotnet test", without.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Testing", without.Packages);
    }

    [Fact]
    public async Task Rask_new_writes_the_test_project_and_no_tests_does_not()
    {
        var (withFs, withTests) = Command();
        var (withoutFs, withoutTests) = Command();

        var withExit = await withTests.ExecuteAsync(["Shop"], CancellationToken.None);
        var withoutExit = await withoutTests.ExecuteAsync(["Shop", "--no-tests"], CancellationToken.None);

        Assert.Equal(0, withExit);
        Assert.Equal(0, withoutExit);
        Assert.True(withFs.FileExists("/proj/Shop/Shop.Tests/Shop.Tests.csproj"));
        Assert.False(withoutFs.FileExists("/proj/Shop/Shop.Tests/Shop.Tests.csproj"));
    }

    [Fact]
    public async Task No_tests_is_refused_on_a_template_that_has_no_test_project()
    {
        var console = new StringConsole();
        var command = new NewCommand(console, new FakeFileSystem(), new FakeProcessRunner(), "/proj");

        var exit = await command.ExecuteAsync(["Shop", "--template", "wasm-hosted", "--no-tests"], CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.Contains("--no-tests", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_lists_no_tests()
    {
        var console = new StringConsole();
        var app = CliApplication.CreateDefault(console, new FakeProcessRunner(), new FakeFileSystem());

        await app.RunAsync(["new", "--help"], CancellationToken.None);

        Assert.Contains("--no-tests", console.OutText, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Server(string[] off)
    {
        _ = TemplateCatalog.TryGet("server", out var template);
        return Relative(ProjectGenerator.GenerateServer(Root, "Shop", NewCommand.ToBatteries(template, off), Version));
    }

    private static Dictionary<string, string> Wasm(string[] off)
    {
        _ = TemplateCatalog.TryGet("wasm", out var template);
        var batteries = NewCommand.ToBatteries(template, off);
        return Relative(ProjectGenerator.GenerateWasm(Root, "Shop", batteries.Pwa, batteries.Docker, Version, batteries));
    }

    private static Dictionary<string, string> Relative(ScaffoldResult result) =>
        result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content,
            StringComparer.Ordinal);

    private static (FakeFileSystem Fs, NewCommand Command) Command()
    {
        var fs = new FakeFileSystem();
        return (fs, new NewCommand(new StringConsole(), fs, new FakeProcessRunner(), "/proj"));
    }
}
