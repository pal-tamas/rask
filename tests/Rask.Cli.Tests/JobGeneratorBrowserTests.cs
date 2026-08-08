using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask generate job</c> is not gated on project kind — it runs wherever a .csproj is found,
///     including a browser app. Its next-steps used to be server-only there, and following them appeared
///     to work: the app built, ran, and silently lost every queued job on reload.
/// </summary>
public class JobGeneratorBrowserTests
{
    [Theory]
    [InlineData("<TargetFramework>net10.0-browser</TargetFramework>")]
    [InlineData("<RaskWasm>true</RaskWasm>")]
    [InlineData("""<ProjectReference Include="..\..\src\Rask.Wasm\Rask.Wasm.csproj"/>""")]
    public void DetectBrowser_RecognisesEachSignalOnItsOwn(string marker)
    {
        Assert.True(ProjectContext.DetectBrowser($"<Project>{marker}</Project>"));
    }

    [Fact]
    public void DetectBrowser_PlainServerProject_IsNotBrowser()
    {
        const string Csproj = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Rask.Server"/></ItemGroup>
            </Project>
            """;

        Assert.False(ProjectContext.DetectBrowser(Csproj));
    }

    // rask db wraps dotnet-ef against a design-time database. A browser bundle has no migrations
    // assembly, so this is not a step the reader can take — printing it sends them somewhere that
    // cannot work.
    [Fact]
    public void Notes_ForABrowserApp_DoNotTellYouToRunRaskDb()
    {
        var notes = JobGenerator.Notes("SendWelcome", DatabaseCatalog.For(DatabaseProvider.Sqlite), isBrowser: true);

        Assert.DoesNotContain("rask db add", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("rask db update", notes, StringComparison.Ordinal);
    }

    // Without this call the database lives in the runtime's in-memory filesystem and every queued job is
    // gone on reload — the failure the old notes walked people into.
    [Fact]
    public void Notes_ForABrowserApp_RegisterTheBrowserDatabase()
    {
        var notes = JobGenerator.Notes("SendWelcome", DatabaseCatalog.For(DatabaseProvider.Sqlite), isBrowser: true);

        Assert.Contains("AddRaskBrowserSqlite", notes, StringComparison.Ordinal);
        Assert.Contains("BrowserSqlite.ConnectionString", notes, StringComparison.Ordinal);
        Assert.Contains("AddRaskJobs<AppDbContext>", notes, StringComparison.Ordinal);
    }

    // Both are silent failures rather than build errors, so the scaffolder is the last place they can be
    // caught before someone spends an afternoon on them.
    [Fact]
    public void Notes_ForABrowserApp_WarnAboutTheTwoBuildSettingsThatBreakItSilently()
    {
        var notes = JobGenerator.Notes("SendWelcome", DatabaseCatalog.For(DatabaseProvider.Sqlite), isBrowser: true);

        Assert.Contains("WasmBuildNative=false", notes, StringComparison.Ordinal);
        Assert.Contains("PublishTrimmed", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Notes_ForAServerApp_AreUnchanged()
    {
        var notes = JobGenerator.Notes("SendWelcome", DatabaseCatalog.For(DatabaseProvider.Sqlite));

        Assert.Contains("rask db add AddJobs && rask db update", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRaskBrowserSqlite", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("WasmBuildNative", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForABrowserApp_AddsTheBrowserDatabasePackage()
    {
        var project = new ProjectContext(
            Path.GetTempPath(), "MyApp", DatabaseProvider.Sqlite, isBrowser: true);

        var result = JobGenerator.Generate(project, Path.GetTempPath(), "SendWelcome", feature: null, outputOverride: null);

        Assert.Contains("Rask.SQLite.Browser", result.Packages);
    }

    [Fact]
    public void Generate_ForAServerApp_DoesNotAddTheBrowserDatabasePackage()
    {
        var project = new ProjectContext(Path.GetTempPath(), "MyApp");

        var result = JobGenerator.Generate(project, Path.GetTempPath(), "SendWelcome", feature: null, outputOverride: null);

        Assert.DoesNotContain("Rask.SQLite.Browser", result.Packages);
    }
}
