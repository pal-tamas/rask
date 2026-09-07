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
/// </remarks>
public class PrerenderCompanionGenerationTests : IDisposable
{
    private readonly string _dir;

    public PrerenderCompanionGenerationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rask-prerender-companion-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "App.cs"), "namespace Fixture; public sealed class App { }");
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
