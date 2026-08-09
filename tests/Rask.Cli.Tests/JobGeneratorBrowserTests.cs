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

    // The Server half of a `wasm-hosted` solution references Rask.Wasm.Hosting (ProjectGenerator.
    // WasmHosted.cs), whose id contains "Rask.Wasm". Reading it as a browser app is the costly direction
    // to be wrong in: that project is precisely where a background job belongs, and misdetecting it both
    // prints the browser next-steps and adds Rask.SQLite.Browser to a server project, which doesn't
    // resolve there — `rask g j` exits 1 with "the wiring didn't complete".
    [Theory]
    [InlineData("""<PackageReference Include="Rask.Wasm.Hosting" Version="0.20.0"/>""")]
    [InlineData("""<ProjectReference Include="..\..\src\Rask.Wasm.Hosting\Rask.Wasm.Hosting.csproj"/>""")]
    public void DetectBrowser_WasmHostedServerProject_IsNotBrowser(string reference)
    {
        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>{reference}</ItemGroup>
            </Project>
            """;

        Assert.False(ProjectContext.DetectBrowser(csproj));
    }

    // <RaskWasm>false</RaskWasm> asserts the opposite of what it was being read as.
    [Fact]
    public void DetectBrowser_RaskWasmExplicitlyFalse_IsNotBrowser() =>
        Assert.False(ProjectContext.DetectBrowser(
            "<Project><PropertyGroup><RaskWasm>false</RaskWasm></PropertyGroup></Project>"));

    // "-browser" is matched on the TargetFramework element, not anywhere in the file: a comment or a
    // package id that happens to contain it says nothing about how the project runs.
    [Theory]
    [InlineData("<!-- the -browser TFM is covered in docs/wasm.md -->")]
    [InlineData("""<PackageReference Include="Some.Vendor-browser.Tools" Version="1.0.0"/>""")]
    public void DetectBrowser_IncidentalMentionOfBrowser_IsNotBrowser(string line)
    {
        var csproj = $"""
            <Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>{line}</Project>
            """;

        Assert.False(ProjectContext.DetectBrowser(csproj));
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
