namespace Rask.Cli.Tests;

/// <summary>
///     Project/template detection for <c>rask dev</c>. Pure over <see cref="FakeFileSystem" />, so every
///     template shape <c>rask new</c> can produce is covered without one existing on disk.
/// </summary>
public sealed class DevTargetTests
{
    private const string ServerCsproj = """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""";
    private const string WasmCsproj = """<Project Sdk="Microsoft.NET.Sdk.WebAssembly"></Project>""";

    private const string LaunchSettings = """
        {
          "profiles": {
            "IIS Express": { "commandName": "IISExpress" },
            "App": {
              "commandName": "Project",
              "launchBrowser": true,
              "applicationUrl": "https://localhost:5001;http://localhost:5000"
            }
          }
        }
        """;

    [Fact]
    public void A_server_project_is_detected_with_its_https_url()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Properties/launchSettings.json", LaunchSettings);

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.NotNull(target);
        Assert.Equal(DevTemplateKind.Server, target!.Kind);
        Assert.Equal("App", target.Name);
        // https is preferred over the http sibling in the same applicationUrl.
        Assert.Equal("https://localhost:5001", target.LaunchUrl);
        Assert.True(target.ProfileLaunchesBrowser);
    }

    [Fact]
    public void A_non_Project_profile_is_skipped()
    {
        // "IIS Express" comes first in the file; only commandName: Project describes what `dotnet run`
        // will actually do.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Properties/launchSettings.json", LaunchSettings);

        Assert.Equal("https://localhost:5001", DevTarget.Detect(fs, "/app", null)!.LaunchUrl);
    }

    [Fact]
    public void The_wasm_hosted_layout_resolves_to_the_server_host()
    {
        // A directory OF projects with no csproj at the root. Running it means running the Server host,
        // which the next-steps text used to make the user type by hand.
        var fs = new FakeFileSystem();
        fs.Seed("/shop/Shop.Client/Shop.Client.csproj", WasmCsproj);
        fs.Seed("/shop/Shop.Server/Shop.Server.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><ProjectReference Include="../Shop.Client/Shop.Client.csproj" /></ItemGroup></Project>""");
        fs.Seed("/shop/Shop.Shared/Shop.Shared.csproj", """<Project Sdk="Microsoft.NET.Sdk"></Project>""");

        var target = DevTarget.Detect(fs, "/shop", null);

        Assert.NotNull(target);
        Assert.Equal("Shop.Server", target!.Name);
        Assert.Equal(DevTemplateKind.WasmHosted, target.Kind);
    }

    [Fact]
    public void A_standalone_wasm_project_has_no_launch_url()
    {
        // That template scaffolds no launchSettings.json at all.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", WasmCsproj);

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.Equal(DevTemplateKind.WasmStandalone, target!.Kind);
        Assert.Null(target.LaunchUrl);
        Assert.False(target.ProfileLaunchesBrowser);
    }

    [Fact]
    public void A_native_project_is_detected_from_its_target_frameworks()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", """<Project><TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks></Project>""");

        Assert.Equal(DevTemplateKind.Native, DevTarget.Detect(fs, "/app", null)!.Kind);
    }

    [Fact]
    public void Detection_walks_up_from_a_subdirectory()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Features/Home/HomePage.cs", "");

        Assert.Equal("App", DevTarget.Detect(fs, "/app/Features/Home", null)!.Name);
    }

    [Fact]
    public void An_explicit_project_beats_detection_and_accepts_a_csproj_or_a_directory()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/other/Other.csproj", ServerCsproj);

        Assert.Equal("Other", DevTarget.Detect(fs, "/app", "/other/Other.csproj")!.Name);
        Assert.Equal("Other", DevTarget.Detect(fs, "/app", "/other")!.Name);
    }

    [Fact]
    public void An_explicit_project_that_does_not_exist_resolves_to_nothing()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);

        Assert.Null(DevTarget.Detect(fs, "/app", "/nope/Nope.csproj"));
    }

    [Fact]
    public void No_project_anywhere_resolves_to_nothing()
    {
        Assert.Null(DevTarget.Detect(new FakeFileSystem(), "/empty", null));
    }

    [Fact]
    public void An_ambiguous_directory_resolves_to_nothing()
    {
        // Two projects side by side: the caller asks for --project rather than guessing.
        var fs = new FakeFileSystem();
        fs.Seed("/app/One.csproj", ServerCsproj);
        fs.Seed("/app/Two.csproj", ServerCsproj);

        Assert.Null(DevTarget.Detect(fs, "/app", null));
    }

    [Fact]
    public void Malformed_launch_settings_do_not_throw()
    {
        // Mirrors how the generate/deploy configs treat a corrupt file: carry on without it rather than
        // failing a command over something cosmetic.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Properties/launchSettings.json", "{ not json");

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.NotNull(target);
        Assert.Null(target!.LaunchUrl);
    }

    [Fact]
    public void Launch_settings_with_no_Project_profile_yield_no_url()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Properties/launchSettings.json", """{ "profiles": { "IIS Express": { "commandName": "IISExpress" } } }""");

        Assert.Null(DevTarget.Detect(fs, "/app", null)!.LaunchUrl);
    }

    [Fact]
    public void An_http_only_profile_yields_that_url()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Properties/launchSettings.json", """
            { "profiles": { "App": { "commandName": "Project", "applicationUrl": "http://localhost:5000" } } }
            """);

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.Equal("http://localhost:5000", target!.LaunchUrl);
        Assert.False(target.ProfileLaunchesBrowser);
    }
}
