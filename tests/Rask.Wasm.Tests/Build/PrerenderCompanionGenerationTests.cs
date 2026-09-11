using System.Diagnostics;

namespace Rask.Wasm.Tests.Build;

/// <summary>
///     Prerendering compiles the app's own sources a second time, for <c>net10.0</c>, out of a companion
///     project generated into <c>obj/</c>. These assert what it wrote.
/// </summary>
/// <remarks>
///     Only the generation step, deliberately — the same trade as
///     <c>BrowserCompanionGenerationTests</c>: actually publishing a prerendered app takes minutes and
///     does not belong in the unit gate, and generation is where the failures are, because the file is
///     assembled out of MSBuild items rather than written by hand.
///     <para>
///         The failure that prompted these: the companion carried the app's references but not its
///         global usings, so an app declaring <c>&lt;Using Include="..."/&gt;</c> in its csproj could not
///         prerender. It surfaces as CS0103 on a name whose assembly IS on the companion's reference
///         list, which reads as the app being broken rather than as a missing using — and it is invisible
///         until someone turns prerendering on.
///     </para>
///     <para>
///         Embedded resources were the same omission again, and quieter still: they cost no compile
///         error at all. The companion built, ran, and threw at RENDER time on every page that reads a
///         resource — which the pass catches and reports as a skip, so the only symptom is a smaller
///         sitemap. Sixteen of this repo's own twenty routes were skipped that way, with a green publish.
///     </para>
/// </remarks>
public class PrerenderCompanionGenerationTests : IDisposable
{
    private readonly string _dir;

    public PrerenderCompanionGenerationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rask-prerender-companion-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "App.cs"), "namespace Fixture; public sealed class App { }");
        Directory.CreateDirectory(Path.Combine(_dir, "Features"));
        File.WriteAllText(Path.Combine(_dir, "Features", "Demo.cs"), "// read back at render time");
        File.WriteAllText(Path.Combine(_dir, "Features", "Notes.txt"), "no LogicalName of its own");
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Fixture</RootNamespace>
                <RaskPrerender>true</RaskPrerender>
              </PropertyGroup>
              <ItemGroup>
                <Using Include="Fixture.Widgets"/>
                <Using Include="Fixture.Helpers" Static="true"/>
                <Using Include="System.Collections.Generic" Alias="Coll"/>
              </ItemGroup>
              <ItemGroup>
                <EmbeddedResource Include="Features/Demo.cs">
                  <LogicalName>raksrc/Demo.cs</LogicalName>
                </EmbeddedResource>
                <EmbeddedResource Include="Features/Notes.txt"/>
              </ItemGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Wasm", "build", "Rask.Wasm.Prerender.targets")}"/>
            </Project>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public void TheAppsOwnGlobalUsingsReachTheCompanion()
    {
        // The bug this file was added for. The companion compiles the app's SOURCES, so a source leaning
        // on a global using the csproj declares does not compile without it — and the resulting CS0103
        // points at the source, not at the missing using.
        var project = Generate();

        Assert.Contains("<Using Include=\"Fixture.Widgets\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticUsingStaysStatic()
    {
        // Carrying the item but dropping the metadata is worse than dropping the item: `using X` and
        // `using static X` are different usings, so the companion would compile a subtly different
        // program and fail somewhere that says nothing about this.
        Assert.Contains(
            "<Using Include=\"Fixture.Helpers\" Static=\"true\" />", Generate(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAliasedUsingKeepsItsAlias()
    {
        Assert.Contains(
            "<Using Include=\"System.Collections.Generic\" Alias=\"Coll\" />",
            Generate(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EachUsingIsEmittedExactlyOnce()
    {
        // Three emission lines partition the set by metadata. Getting the conditions wrong the other way
        // duplicates an item rather than dropping it, which is a build error in the companion — but one
        // that names the generated file, not the app, so it is worth pinning here instead.
        var project = Generate();

        Assert.Equal(1, Occurrences(project, "Include=\"Fixture.Widgets\""));
        Assert.Equal(1, Occurrences(project, "Include=\"Fixture.Helpers\""));
        Assert.Equal(1, Occurrences(project, "Include=\"System.Collections.Generic\""));
    }

    [Fact]
    public void AnEmbeddedResourceKeepsItsLogicalName()
    {
        // A resource is found by NAME at runtime. The companion is a different assembly in a different
        // directory, so re-globbing the file is not enough — the name has to travel with it, or the
        // lookup fails with "not found in any registered assembly" on a file that is plainly embedded.
        var project = Generate();

        Assert.Contains("<EmbeddedResource Include=\"", project, StringComparison.Ordinal);
        Assert.Contains("LogicalName=\"raksrc/Demo.cs\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AResourceWithNoLogicalNameIsLinkedBackToTheAppsLayout()
    {
        // Without a LogicalName the SDK computes the manifest name from RootNamespace plus the path
        // RELATIVE TO THE PROJECT — and the companion's project directory is the app's obj/, so the
        // computed name would differ. Link pins it back to where the app has the file.
        Assert.Contains("Link=\"Features/Notes.txt\"", Generate().Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublicApiGateDoesNotCoverTheCompanion()
    {
        // The gate in Directory.Build.targets covers every project under src/, and the companion is
        // GENERATED into the app's obj/ — which is under src/. It can never carry a baseline, because
        // nothing tracks a file the build rewrites, and the app's own opt-out does not reach a project
        // generated from it. Missing, the gate failed the publish with "no baseline for net10.0" and
        // took the entire browser E2E gate down before one test ran. It only shows on a clean obj/,
        // so a warm one hides it — which is exactly why it is pinned here and not left to the gate.
        Assert.Contains(
            "<RaskPublicApiTracked>false</RaskPublicApiTracked>",
            Generate(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSdksOwnResourceGlobIsOff()
    {
        // The companion's project directory sits inside the app's obj/. Left on, the SDK's default
        // EmbeddedResource glob would sweep up whatever a previous build left there and embed it.
        Assert.Contains(
            "<EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>",
            Generate(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompanionDoesNotBuildTheAppsIslands()
    {
        // The companion compiles the app's C#, so it sees every island declared there, but Rask.External
        // globs for their front-end files from the companion's own directory inside obj/ and finds none.
        // Left on, the prop-types step warned RASKISLAND004 for every island on every prerendered publish,
        // about chunks the app's own build had just bundled (#1068).
        var project = Generate();

        Assert.Contains("<RaskExternalPropTypes>false</RaskExternalPropTypes>", project, StringComparison.Ordinal);
        Assert.Contains("<RaskExternalBuild>false</RaskExternalBuild>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EachResourceIsEmittedExactlyOnce()
    {
        // Two emission lines partition the set on whether the item names itself. A condition wrong the
        // other way emits both twice, which the companion then fails to build on — naming the generated
        // file rather than the app.
        var project = Generate();

        Assert.Equal(1, Occurrences(project, "Demo.cs\" LogicalName="));
        Assert.Equal(1, Occurrences(project.Replace('\\', '/'), "Link=\"Features/Notes.txt\""));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

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
        psi.ArgumentList.Add("-t:RaskGeneratePrerenderCompanion");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-v:quiet");
        psi.ArgumentList.Add("-nodeReuse:false");

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"generation failed:\n{stdout}\n{stderr}");

        var generated = Path.Combine(_dir, "obj", "rask-prerender", "App.Prerender.csproj");
        Assert.True(File.Exists(generated), $"no companion was generated:\n{stdout}");
        return File.ReadAllText(generated);
    }

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Wasm")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
