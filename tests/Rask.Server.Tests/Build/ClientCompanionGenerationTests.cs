using System.Text.RegularExpressions;
using Rask.TestFiles;

namespace Rask.Server.Tests.Build;

/// <summary>
///     The one-project build generates the browser app's project into <c>obj/</c> from <c>Client/</c> and
///     <c>Shared/</c>. These assert what it wrote, and what the server half leaves out.
/// </summary>
/// <remarks>
///     Only the generation step, deliberately: publishing the companion links a WebAssembly runtime and
///     takes minutes, which is too slow for the unit gate — <c>ClientPublishE2ETests</c> does that. Generation
///     is also where the failures were: the file is assembled out of MSBuild items, and MSBuild reads an
///     item's <c>Include</c> as a file glob and splits it on semicolons. Both bit, and both were silent.
/// </remarks>
public sealed partial class ClientCompanionGenerationTests : IDisposable
{
    private readonly string _dir;

    public ClientCompanionGenerationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rask-client-companion-" + Guid.NewGuid().ToString("N")[..8]);
        Write("Program.cs", "// the server's entry point; the companion must not compile this");
        Write("Server/PricingRule.cs", "namespace Fixture.Server; public static class PricingRule { }");
        Write("Shared/GetPrice.cs", "namespace Fixture.Shared; public sealed record GetPrice(int Id);");
        Write("Client/Program.cs", "// the browser's entry point");
        Write("Client/App.cs", "namespace Fixture.Client; public sealed class App { }");
        Write("Client/wwwroot/index.html", "<!doctype html><body data-rask-root></body>");
        WriteProject(clientSwitch: null);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public async Task The_companion_compiles_the_client_and_shared_folders_and_nothing_else()
    {
        var project = Slashes(await Generate());

        Assert.Contains("/Client/**/*.cs\" />", project, StringComparison.Ordinal);
        Assert.Contains("/Shared/**/*.cs\" />", project, StringComparison.Ordinal);

        // Every wildcard compile names one of the two folders. A bare project-root glob would ship the
        // server's handlers, and with them its connection strings and rules, to the browser.
        foreach (Match include in CompileGlob().Matches(project))
        {
            var path = include.Groups["path"].Value;
            Assert.True(
                path.Contains("/Client/", StringComparison.Ordinal) || path.Contains("/Shared/", StringComparison.Ordinal),
                $"the companion compiles {path}, which is outside Client/ and Shared/");
        }
    }

    [Fact]
    public async Task The_browser_app_brings_its_own_entry_point()
    {
        // Client/Program.cs IS the entry point, compiled with the rest of Client/. Nothing is generated in
        // its place, so there is no second startup type to name and no hidden file to debug.
        var project = await Generate();

        Assert.DoesNotContain("Program.g.cs", project, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(CompanionDir(), "Program.g.cs")));
    }

    [Fact]
    public async Task A_client_only_reference_reaches_the_bundle()
    {
        // One project, two halves, one reference list — and some pairs exist precisely so that neither half
        // carries the other's transport. Rask.Cqrs.Client in the server would ship endpoint-CALLING code
        // into the process that answers those endpoints.
        Assert.Contains(
            "<PackageReference Include=\"Rask.Cqrs.Client\" Version=\"9.9.9\" />",
            await Generate(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Semicolons_survive_into_the_generated_project()
    {
        Assert.Contains("Edits are lost; change the app instead.", await Generate(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_using_the_app_names_for_the_client_reaches_it_and_a_package_injected_one_does_not()
    {
        var project = await Generate();

        Assert.Contains("<Using Include=\"Fixture.Shared\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Using Include=\"Fixture.Aliased\" Alias=\"Shorthand\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Using Include=\"Fixture.Statics\" Static=\"true\" />", project, StringComparison.Ordinal);

        // @(Using) also carries what referenced PACKAGES inject, several of which exist only on the server.
        Assert.DoesNotContain("Fixture.InjectedByAPackage", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_boot_page_is_the_apps_own_and_the_sdk_fills_it()
    {
        // The browser loads Client/wwwroot/index.html. The SDK only fills the import map of a page in the
        // project's own web root, so the page is copied there and the placeholders are turned on.
        var project = await Generate();

        Assert.Contains("<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>", project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(CompanionDir(), "wwwroot", "index.html")));
    }

    [Fact]
    public async Task Scoped_assets_come_from_the_client_folder()
    {
        // The companion's own folder holds no components. Its glob is off, the items name Client/, and
        // tsgo is rooted there so the emitted files line up with the items.
        var project = Slashes(await Generate());

        Assert.Contains("<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>", project, StringComparison.Ordinal);
        Assert.Contains("<RaskScopedTsGlob>false</RaskScopedTsGlob>", project, StringComparison.Ordinal);
        Assert.Contains("/Client/**/*.css\" />", project, StringComparison.Ordinal);
        Assert.Contains("/Client/**/*.ts\" />", project, StringComparison.Ordinal);
        Assert.Matches(new Regex("<RaskScopedTsRootDir>.*/Client</RaskScopedTsRootDir>"), project);
    }

    [Fact]
    public async Task A_package_islands_snapshot_reaches_the_browser_app()
    {
        // A package island's chain steps are generated from the committed {Island}.props.json beside its class.
        // Rask.External globs those from the companion's own directory, inside obj/, where none lives — so without
        // the app's, the island compiles with no steps and the browser half fails on its first one as CS1929.
        var project = Slashes(await Generate());

        Assert.Contains("/Client/**/*.props.json\" />", project, StringComparison.Ordinal);
        Assert.Contains("/Shared/**/*.props.json\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_companion_publishes_outside_its_own_project_directory()
    {
        // Publishing into the companion's own folder makes each publish an input to the next.
        await Generate();

        var companionDir = CompanionDir();
        var outputDir = Path.Combine(_dir, "obj", "rask-client-out", "net10.0-browser");

        Assert.True(Directory.Exists(companionDir));
        Assert.False(
            outputDir.StartsWith(companionDir + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "the companion's output directory must not sit inside its project directory");
    }

    [Fact]
    public async Task The_server_compiles_everything_except_the_client()
    {
        var compile = Slashes(await Evaluate("-getItem:Compile"));

        Assert.Contains("Program.cs", compile, StringComparison.Ordinal);
        Assert.Contains("Server/PricingRule.cs", compile, StringComparison.Ordinal);
        Assert.Contains("Shared/GetPrice.cs", compile, StringComparison.Ordinal);
        Assert.DoesNotContain("Client/", compile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_Program_cs_switches_it_on_and_RaskClient_false_switches_it_off()
    {
        Assert.Equal("true", (await Evaluate("-getProperty:RaskClient")).Trim());

        WriteProject(clientSwitch: false);

        Assert.Equal("false", (await Evaluate("-getProperty:RaskClient")).Trim());
        Assert.Contains("Client/App.cs", Slashes(await Evaluate("-getItem:Compile")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_boot_page_is_refused_by_name()
    {
        File.Delete(Path.Combine(_dir, "Client", "wwwroot", "index.html"));

        var result = await Run("-t:RaskGenerateClientCompanion", "-v:minimal");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Client/wwwroot/index.html is missing", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net10.0", "net10.0-browser")]
    [InlineData("net11.0", "net11.0-browser")]
    public async Task The_browser_app_targets_the_server_halfs_dotnet_version(string server, string bundle)
    {
        // The companion compiles the app's Client/ sources, so it builds them for the .NET version the app
        // targets — and the development manifest MapRaskSpa serves is looked for under that framework's bin
        // folder. A literal net10.0-browser compiled a net11.0 app for an older framework than its server.
        // Each framework's project is generated into a folder of its own, which is where Generate looks.
        var csproj = Path.Combine(_dir, "App.csproj");
        File.WriteAllText(csproj, File.ReadAllText(csproj).Replace(
            "<TargetFramework>net10.0</TargetFramework>",
            $"<TargetFramework>{server}</TargetFramework>",
            StringComparison.Ordinal));

        Assert.Contains($"<TargetFramework>{bundle}</TargetFramework>", await Generate(bundle), StringComparison.Ordinal);
        Assert.Equal(bundle, (await Evaluate("-getProperty:_RaskClientFramework")).Trim());
    }

    [Fact]
    public async Task The_server_half_leaves_the_editors_engine_to_its_browser_app()
    {
        // One project, built twice, and the browser half's wwwroot is merged into the server's publish. Both
        // halves writing wwwroot/js/rask-ui-editor.js is one relative path twice: NETSDK1152, and no
        // wasm-hosted app publishes. The browser half owns it — the page that mounts an editor runs there,
        // and MapRaskSpa serves that half's files in a build and in a publish alike.
        WriteProject(clientSwitch: null, WebSdk);

        var result = await Run("-t:RaskUiWriteEditor", "-v:minimal");
        var content = Slashes(await Evaluate("-getItem:Content"));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.False(File.Exists(Path.Combine(_dir, EditorEngine)), "the server half wrote the engine too");
        Assert.False(File.Exists(Path.Combine(_dir, EditorNotices)), "the server half wrote the notices too");
        Assert.DoesNotContain("rask-ui-editor", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_app_with_no_browser_half_writes_the_editors_engine_with_nothing_said()
    {
        // The control for the test above: the same fixture with its client switched off is an ordinary
        // Server app, and that one gets the file — so the skip above is the client's doing, not the fixture's.
        WriteProject(clientSwitch: false, WebSdk);

        var result = await Run("-t:RaskUiWriteEditor", "-v:minimal");
        var content = Slashes(await Evaluate("-getItem:Content"));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(Path.Combine(_dir, EditorEngine)), result.Output);
        Assert.True(File.Exists(Path.Combine(_dir, EditorNotices)), result.Output);
        Assert.Contains(EditorEngine, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_engine_an_earlier_build_left_in_the_server_half_is_removed_and_not_published()
    {
        // A build from before the server half stopped writing it left the file in the app's own wwwroot,
        // where the Web SDK's glob finds it: published beside the browser half's, it is the collision again.
        Write(EditorEngine, "// left by an earlier build");
        Write(EditorNotices, "left by an earlier build");
        WriteProject(clientSwitch: null, WebSdk);

        var content = Slashes(await Evaluate("-getItem:Content"));
        var result = await Run("-t:RaskUiLeaveEditorToClient", "-v:minimal");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain("rask-ui-editor", content, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir, EditorEngine)));
        Assert.False(File.Exists(Path.Combine(_dir, EditorNotices)));
    }

    [Fact]
    public async Task The_browser_app_writes_the_editors_engine_unless_the_app_said_it_draws_none()
    {
        WriteProject(clientSwitch: null, WebSdk);
        var unsaid = await Generate();

        WriteProject(clientSwitch: null, WebSdk, editorEngine: false);
        var optedOut = await Generate();

        // Nothing said: the browser half is a WebAssembly SDK app, which gets the engine by default.
        Assert.DoesNotContain("RaskUiEditorEngine", unsaid, StringComparison.Ordinal);
        // The app's one switch has to reach the half that does the writing, or it would turn nothing off.
        Assert.Contains("<RaskUiEditorEngine>false</RaskUiEditorEngine>", optedOut, StringComparison.Ordinal);
    }

    [GeneratedRegex("<Compile Include=\"(?<path>[^\"]*\\*\\*[^\"]*)\" />")]
    private static partial Regex CompileGlob();

    private const string WebSdk = "Microsoft.NET.Sdk.Web";
    private const string EditorEngine = "wwwroot/js/rask-ui-editor.js";
    private const string EditorNotices = "wwwroot/js/rask-ui-editor.LICENSES.txt";

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // The kit's targets are imported as its package would import them, and the kit is "referenced" by name
    // only: nothing here compiles, so the reference is never resolved.
    private void WriteProject(bool? clientSwitch, string sdk = "Microsoft.NET.Sdk", bool? editorEngine = null) =>
        Write("App.csproj", $"""
            <Project Sdk="{sdk}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Fixture</RootNamespace>
                <RaskClientCompanionSrc>{SrcDir}</RaskClientCompanionSrc>
                {(clientSwitch is { } on ? $"<RaskClient>{on.ToString().ToLowerInvariant()}</RaskClient>" : "")}
                {(editorEngine is { } engine ? $"<RaskUiEditorEngine>{engine.ToString().ToLowerInvariant()}</RaskUiEditorEngine>" : "")}
              </PropertyGroup>
              <ItemGroup>
                <RaskClientPackageReference Include="Rask.Cqrs.Client" Version="9.9.9"/>
                <RaskClientUsing Include="Fixture.Shared"/>
                <RaskClientUsing Include="Fixture.Aliased" Alias="Shorthand"/>
                <RaskClientUsing Include="Fixture.Statics" Static="true"/>
                <!-- Stands in for what a referenced package's build/*.props injects. It must NOT cross. -->
                <Using Include="Fixture.InjectedByAPackage"/>
                <ProjectReference Include="{Path.Combine(SrcDir, "Rask.Ui", "Rask.Ui.csproj")}"/>
              </ItemGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Server", "build", "Rask.Server.Client.targets")}"/>
              <Import Project="{Path.Combine(SrcDir, "Rask.Ui", "build", "Rask.Ui.targets")}"/>
            </Project>
            """);

    // The generated project carries MSBuild's canonical '\\' separators, which MSBuild normalizes when it
    // evaluates them — so the file works on every platform and only these assertions care.
    private static string Slashes(string s) => s.Replace('\\', '/');

    // One folder per browser framework, so the per-framework builds of a multi-targeted server never share one.
    private string CompanionDir(string framework = "net10.0-browser") =>
        Path.Combine(_dir, "obj", "rask-client", framework);

    private async Task<string> Generate(string framework = "net10.0-browser")
    {
        var result = await Run("-t:RaskGenerateClientCompanion", "-v:quiet");
        Assert.True(result.ExitCode == 0, $"generation failed:\n{result.Output}");

        var generated = Path.Combine(CompanionDir(framework), "App.Client.csproj");
        Assert.True(File.Exists(generated), $"no companion was generated:\n{result.Output}");
        return File.ReadAllText(generated);
    }

    // Straight from MSBuild's own evaluation, rather than from the generated companion.
    private async Task<string> Evaluate(string query)
    {
        var result = await Run(query);
        Assert.True(result.ExitCode == 0, $"evaluating {query} failed:\n{result.Output}");
        return result.Output;
    }

    private Task<TestProcessResult> Run(params string[] args) =>
        TestProcess.Run(
            "dotnet",
            ["msbuild", "App.csproj", .. args, "-nologo", "-nodeReuse:false"],
            _dir,
            cancellationToken: TestContext.Current.CancellationToken);

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Server")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
