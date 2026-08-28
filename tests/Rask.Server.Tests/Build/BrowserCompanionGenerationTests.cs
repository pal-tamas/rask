using System.Diagnostics;

namespace Rask.Server.Tests.Build;

/// <summary>
///     The one-project build generates the browser half's project into <c>obj/</c>. These assert what
///     it wrote.
/// </summary>
/// <remarks>
///     Only the generation step, deliberately: publishing the companion links a WebAssembly runtime
///     and takes minutes, which is too slow to sit in the unit gate. Generation is also where the
///     failures were — the file is assembled out of MSBuild items, and MSBuild reads an item's
///     <c>Include</c> as a file glob and splits it on semicolons. Both bit, and both were silent: the
///     wildcard line matched nothing on disk and vanished, taking the app's own sources out of the
///     build, and every line carrying a semicolon was cut in two.
/// </remarks>
public class BrowserCompanionGenerationTests : IDisposable
{
    private readonly string _dir;


    public BrowserCompanionGenerationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rask-companion-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "App.cs"), "namespace Fixture; public sealed class App { }");
        File.WriteAllText(Path.Combine(_dir, "Program.cs"), "// the server half; the companion must not compile this");
        Directory.CreateDirectory(Path.Combine(_dir, "Browser"));
        File.WriteAllText(
            Path.Combine(_dir, "Browser", "BrowserStartup.cs"),
            "namespace Fixture.Browser; public static class BrowserStartup { }");
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Fixture</RootNamespace>
                <RaskBrowserRung>true</RaskBrowserRung>
                <RaskBrowserCompanionSrc>{SrcDir}</RaskBrowserCompanionSrc>
                <RaskBrowserStartup>Fixture.BrowserStartup</RaskBrowserStartup>
              </PropertyGroup>
              <ItemGroup>
                <RaskBrowserPackageReference Include="Rask.Cqrs.Client" Version="9.9.9"/>
              </ItemGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Server", "build", "Rask.Server.Browser.targets")}"/>
            </Project>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public void TheCompanionCompilesTheAppsOwnSources()
    {
        // The line that vanished. Without it the companion compiles only its generated entry point,
        // which then fails on a root component that is not in the compilation — a confusing error a
        // long way from the wildcard that caused it.
        var project = Slashes(Generate());

        // Asserted on the tail rather than the whole path: the absolute prefix is whatever MSBuild
        // canonicalised the fixture's directory to, and it is not what this is about.
        Assert.Contains("<Compile Include=\"", project, StringComparison.Ordinal);
        Assert.Contains("/**/*.cs\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrowserOnlyReferenceReachesTheBundleAndNotTheServer()
    {
        // One project, two halves, one reference list — and some pairs exist precisely so that neither
        // half carries the other's transport. Rask.Cqrs.Client in the server would ship
        // endpoint-CALLING code into the process that answers those endpoints.
        var project = Generate();

        Assert.Contains(
            "<PackageReference Include=\"Rask.Cqrs.Client\" Version=\"9.9.9\" />",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStartupHookIsCalledBeforeTheAppRuns()
    {
        // The browser half has no Program.cs of its own — that file is the server's, and the companion
        // excludes it — so without this there is nowhere to register anything the bundle needs.
        Generate();
        var program = File.ReadAllText(Path.Combine(_dir, "obj", "rask-browser", "Program.g.cs"));

        Assert.Contains("Fixture.BrowserStartup.Configure(host.Services);", program, StringComparison.Ordinal);

        // And it must run BEFORE the app does, or the registrations miss the first render.
        Assert.True(
            program.IndexOf("Configure(host.Services)", StringComparison.Ordinal)
            < program.IndexOf("host.RunAsync<", StringComparison.Ordinal),
            "the startup hook must be called before RunAsync");
    }

    [Fact]
    public void TheServerHalfIsLeftOut()
    {
        // Program.cs is the server's entry point and Server/ is the convention for code that only
        // exists there. Compiling either into a browser bundle is what the split exists to avoid.
        var project = Slashes(Generate());

        Assert.Contains("/Program.cs\" />", project, StringComparison.Ordinal);
        Assert.Contains("/Server/**\" />", project, StringComparison.Ordinal);
        Assert.Contains("/obj/**\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void SemicolonsSurviveIntoTheGeneratedFiles()
    {
        // MSBuild splits an item's Include on ';'. A line cut in two produces invalid XML or, as here,
        // a C# statement with its terminator on the next line — which the compiler then reports
        // against a generated file nobody wrote.
        var project = Generate();
        var program = File.ReadAllText(Path.Combine(_dir, "obj", "rask-browser", "Program.g.cs"));

        Assert.Contains("Edits are lost; change the app instead.", project, StringComparison.Ordinal);
        Assert.Contains("RunAsync<Fixture.App>();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBundleIsNotFingerprinted()
    {
        // A takeover boots from a server-rendered page, which carries no import map — so a fingerprinted
        // bundle resolves _framework/dotnet.js to a path that exists in a build and not in a publish.
        Assert.Contains("<WasmFingerprintAssets>false</WasmFingerprintAssets>", Generate(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompanionPublishesOutsideItsOwnProjectDirectory()
    {
        // Publishing into the companion's own folder makes each publish an input to the next: the
        // bundle's main.js and dotnet.js come back as candidate scoped-JS and fail the build. The first
        // publish succeeds and every one after it fails.
        Generate();

        var companionDir = Path.Combine(_dir, "obj", "rask-browser");
        var outputDir = Path.Combine(_dir, "obj", "rask-browser-out");

        Assert.True(Directory.Exists(companionDir));
        Assert.False(
            outputDir.StartsWith(companionDir + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "the companion's output directory must not sit inside its project directory");
    }

    [Fact]
    public void TheServerDoesNotCompileTheBrowserHalfsOwnCode()
    {
        // The mirror of Server/, and the reason RaskBrowserPackageReference is usable at all: that
        // reference reaches the companion ALONE, so a file using it has to be somewhere the server does
        // not compile. Without this exclusion the browser-only reference is a seam nothing can sit on —
        // the file lands in both halves and fails in the one missing the package.
        var compile = Slashes(ServerCompileItems());

        Assert.Contains("App.cs", compile, StringComparison.Ordinal);
        Assert.DoesNotContain("Browser/BrowserStartup.cs", compile, StringComparison.Ordinal);
    }

    // What the SERVER half compiles, straight from MSBuild's own evaluation rather than from the
    // generated companion — the exclusion under test is the one that never reaches that file.
    private string ServerCompileItems()
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add("App.csproj");
        psi.ArgumentList.Add("-getItem:Compile");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-nodeReuse:false");

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"evaluating the server's Compile items failed:\n{stdout}\n{stderr}");
        return stdout;
    }

    // The generated project carries MSBuild's canonical '\\' separators, which MSBuild itself normalizes
    // when it evaluates them — so the file works on every platform and only these assertions care.
    private static string Slashes(string s) => s.Replace('\\', '/');

    private string Generate()
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add("App.csproj");
        psi.ArgumentList.Add("-t:RaskGenerateBrowserCompanion");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-v:quiet");
        psi.ArgumentList.Add("-nodeReuse:false");

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"generation failed:\n{stdout}\n{stderr}");

        var generated = Path.Combine(_dir, "obj", "rask-browser", "App.Browser.csproj");
        Assert.True(File.Exists(generated), $"no companion was generated:\n{stdout}");
        return File.ReadAllText(generated);
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
