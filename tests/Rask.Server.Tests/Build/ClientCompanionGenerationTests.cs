using System.Diagnostics;
using System.Text.RegularExpressions;

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
    public void The_companion_compiles_the_client_and_shared_folders_and_nothing_else()
    {
        var project = Slashes(Generate());

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
    public void The_browser_app_brings_its_own_entry_point()
    {
        // Client/Program.cs IS the entry point, compiled with the rest of Client/. Nothing is generated in
        // its place, so there is no second startup type to name and no hidden file to debug.
        var project = Generate();

        Assert.DoesNotContain("Program.g.cs", project, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir, "obj", "rask-client", "Program.g.cs")));
    }

    [Fact]
    public void A_client_only_reference_reaches_the_bundle()
    {
        // One project, two halves, one reference list — and some pairs exist precisely so that neither half
        // carries the other's transport. Rask.Cqrs.Client in the server would ship endpoint-CALLING code
        // into the process that answers those endpoints.
        Assert.Contains(
            "<PackageReference Include=\"Rask.Cqrs.Client\" Version=\"9.9.9\" />",
            Generate(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Semicolons_survive_into_the_generated_project()
    {
        Assert.Contains("Edits are lost; change the app instead.", Generate(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_using_the_app_names_for_the_client_reaches_it_and_a_package_injected_one_does_not()
    {
        var project = Generate();

        Assert.Contains("<Using Include=\"Fixture.Shared\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Using Include=\"Fixture.Aliased\" Alias=\"Shorthand\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Using Include=\"Fixture.Statics\" Static=\"true\" />", project, StringComparison.Ordinal);

        // @(Using) also carries what referenced PACKAGES inject, several of which exist only on the server.
        Assert.DoesNotContain("Fixture.InjectedByAPackage", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_page_is_the_apps_own_and_the_sdk_fills_it()
    {
        // The browser loads Client/wwwroot/index.html. The SDK only fills the import map of a page in the
        // project's own web root, so the page is copied there and the placeholders are turned on.
        var project = Generate();

        Assert.Contains("<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>", project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_dir, "obj", "rask-client", "wwwroot", "index.html")));
    }

    [Fact]
    public void Scoped_assets_come_from_the_client_folder()
    {
        // The companion's own folder holds no components. Its glob is off, the items name Client/, and
        // tsgo is rooted there so the emitted files line up with the items.
        var project = Slashes(Generate());

        Assert.Contains("<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>", project, StringComparison.Ordinal);
        Assert.Contains("<RaskScopedTsGlob>false</RaskScopedTsGlob>", project, StringComparison.Ordinal);
        Assert.Contains("/Client/**/*.css\" />", project, StringComparison.Ordinal);
        Assert.Contains("/Client/**/*.ts\" />", project, StringComparison.Ordinal);
        Assert.Matches(new Regex("<RaskScopedTsRootDir>.*/Client</RaskScopedTsRootDir>"), project);
    }

    [Fact]
    public void The_companion_publishes_outside_its_own_project_directory()
    {
        // Publishing into the companion's own folder makes each publish an input to the next.
        Generate();

        var companionDir = Path.Combine(_dir, "obj", "rask-client");
        var outputDir = Path.Combine(_dir, "obj", "rask-client-out");

        Assert.True(Directory.Exists(companionDir));
        Assert.False(
            outputDir.StartsWith(companionDir + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "the companion's output directory must not sit inside its project directory");
    }

    [Fact]
    public void The_server_compiles_everything_except_the_client()
    {
        var compile = Slashes(Evaluate("-getItem:Compile"));

        Assert.Contains("Program.cs", compile, StringComparison.Ordinal);
        Assert.Contains("Server/PricingRule.cs", compile, StringComparison.Ordinal);
        Assert.Contains("Shared/GetPrice.cs", compile, StringComparison.Ordinal);
        Assert.DoesNotContain("Client/", compile, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_Program_cs_switches_it_on_and_RaskClient_false_switches_it_off()
    {
        Assert.Equal("true", Evaluate("-getProperty:RaskClient").Trim());

        WriteProject(clientSwitch: false);
        Assert.Equal("false", Evaluate("-getProperty:RaskClient").Trim());
        Assert.Contains("Client/App.cs", Slashes(Evaluate("-getItem:Compile")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_boot_page_is_refused_by_name()
    {
        File.Delete(Path.Combine(_dir, "Client", "wwwroot", "index.html"));

        var (exit, output) = Run("-t:RaskGenerateClientCompanion", "-v:minimal");

        Assert.NotEqual(0, exit);
        Assert.Contains("Client/wwwroot/index.html is missing", output, StringComparison.Ordinal);
    }

    [GeneratedRegex("<Compile Include=\"(?<path>[^\"]*\\*\\*[^\"]*)\" />")]
    private static partial Regex CompileGlob();

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void WriteProject(bool? clientSwitch) =>
        Write("App.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Fixture</RootNamespace>
                <RaskClientCompanionSrc>{SrcDir}</RaskClientCompanionSrc>
                {(clientSwitch is { } on ? $"<RaskClient>{on.ToString().ToLowerInvariant()}</RaskClient>" : "")}
              </PropertyGroup>
              <ItemGroup>
                <RaskClientPackageReference Include="Rask.Cqrs.Client" Version="9.9.9"/>
                <RaskClientUsing Include="Fixture.Shared"/>
                <RaskClientUsing Include="Fixture.Aliased" Alias="Shorthand"/>
                <RaskClientUsing Include="Fixture.Statics" Static="true"/>
                <!-- Stands in for what a referenced package's build/*.props injects. It must NOT cross. -->
                <Using Include="Fixture.InjectedByAPackage"/>
              </ItemGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Server", "build", "Rask.Server.Client.targets")}"/>
            </Project>
            """);

    // The generated project carries MSBuild's canonical '\\' separators, which MSBuild normalizes when it
    // evaluates them — so the file works on every platform and only these assertions care.
    private static string Slashes(string s) => s.Replace('\\', '/');

    private string Generate()
    {
        var (exit, output) = Run("-t:RaskGenerateClientCompanion", "-v:quiet");
        Assert.True(exit == 0, $"generation failed:\n{output}");

        var generated = Path.Combine(_dir, "obj", "rask-client", "App.Client.csproj");
        Assert.True(File.Exists(generated), $"no companion was generated:\n{output}");
        return File.ReadAllText(generated);
    }

    // Straight from MSBuild's own evaluation, rather than from the generated companion.
    private string Evaluate(string query)
    {
        var (exit, output) = Run(query);
        Assert.True(exit == 0, $"evaluating {query} failed:\n{output}");
        return output;
    }

    private (int Exit, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add("App.csproj");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-nodeReuse:false");

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr);
    }

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
