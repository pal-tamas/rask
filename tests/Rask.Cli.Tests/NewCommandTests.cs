using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class NewCommandTests
{
    private const string WorkingDirectory = "/proj";

    [Theory]
    [InlineData("0.17.0", "0.17.0")]      // a published stable pins exactly
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("0.18.0-alpha.0.5", "0.17.0")] // a dev/CI prerelease isn't on NuGet → fall back
    [InlineData("0.0.0", "0.17.0")]       // the no-version sentinel → fall back
    [InlineData("", "0.17.0")]
    public void ResolvePackageVersion_falls_back_for_unpublishable_versions(string cliVersion, string expected) =>
        Assert.Equal(expected, NewCommand.ResolvePackageVersion(cliVersion));

    [Fact]
    public async Task Server_template_is_generated_directly_without_dotnet_new()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "server", "--auth"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        // Files are written directly under ./MyApp.
        Assert.True(fs.FileExists("/proj/MyApp/MyApp.csproj"));
        Assert.True(fs.FileExists("/proj/MyApp/Program.cs"));
        Assert.True(fs.FileExists("/proj/MyApp/Auth/CredentialStore.cs")); // --auth
        // It restores, and never shells to `dotnet new` / installs Rask.Templates.
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("new"));
    }

    [Fact]
    public async Task Wasm_template_is_generated_directly_without_dotnet_new()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Spa", "--template", "wasm", "--pwa"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        Assert.True(fs.FileExists("/proj/Spa/Spa.csproj"));
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/index.html"));
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/icon.svg")); // --pwa
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("new"));
    }

    [Fact]
    public async Task Server_generation_refuses_to_overwrite_an_existing_project()
    {
        var (console, fs, runner, command) = Build();
        fs.Seed("/proj/MyApp/MyApp.csproj", "<Project/>");

        var exit = await command.ExecuteAsync(["MyApp"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("already exists", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Native_template_is_generated_directly_without_dotnet_new()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MobileApp", "--template", "native", "--host", "server"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        // Files are written directly under ./MobileApp — the server-host heads, not the local ones.
        Assert.True(fs.FileExists("/proj/MobileApp/MobileApp.csproj"));
        Assert.True(fs.FileExists("/proj/MobileApp/Platforms/iOS/ServerAppDelegate.cs"));
        Assert.True(fs.FileExists("/proj/MobileApp/Platforms/Android/ServerActivity.cs"));
        Assert.False(fs.FileExists("/proj/MobileApp/App.cs")); // local-only
        // It restores, and never shells to `dotnet new` / installs Rask.Templates.
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("new"));
    }

    [Fact]
    public async Task Native_defaults_to_the_local_host()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MobileApp", "--template", "native"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        Assert.True(fs.FileExists("/proj/MobileApp/App.cs"));                         // local shared component
        Assert.True(fs.FileExists("/proj/MobileApp/Platforms/iOS/AppDelegate.cs"));   // local head
        Assert.False(fs.FileExists("/proj/MobileApp/Platforms/iOS/ServerAppDelegate.cs"));
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("new"));
    }

    [Fact]
    public async Task Native_rejects_an_invalid_host()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MobileApp", "--template", "native", "--host", "cloud"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Invalid --host 'cloud'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_option_is_rejected_for_non_native_templates()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "server", "--host", "local"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("does not support --host", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WasmHosted_template_is_generated_directly_without_dotnet_new()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["HostedApp", "--template", "wasm-hosted", "--auth"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        // A three-project solution is written directly under ./HostedApp.
        Assert.True(fs.FileExists("/proj/HostedApp/HostedApp.sln"));
        Assert.True(fs.FileExists("/proj/HostedApp/HostedApp.Client/HostedApp.Client.csproj"));
        Assert.True(fs.FileExists("/proj/HostedApp/HostedApp.Server/HostedApp.Server.csproj"));
        Assert.True(fs.FileExists("/proj/HostedApp/HostedApp.Shared/HostedApp.Shared.csproj"));
        Assert.True(fs.FileExists("/proj/HostedApp/HostedApp.Server/Auth/CredentialStore.cs")); // --auth
        // It restores the solution, and never shells to `dotnet new` / installs Rask.Templates.
        Assert.Contains(runner.Invocations, i => i.Arguments is ["restore", "/proj/HostedApp/HostedApp.sln"]);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("new"));
    }

    [Fact]
    public async Task WasmHosted_generation_refuses_to_overwrite_an_existing_solution()
    {
        var (console, fs, runner, command) = Build();
        fs.Seed("/proj/HostedApp/HostedApp.sln", "solution");

        var exit = await command.ExecuteAsync(["HostedApp", "--template", "wasm-hosted"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("already exists", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Missing_name_fails_without_running_dotnet()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("name is required", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_template_fails()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "svelte"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Unknown template 'svelte'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_flag_for_template_fails_with_guidance()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "wasm", "--cqrs"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("does not support: --cqrs", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_option_fails()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--frobnicate"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--frobnicate", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_name_on_a_terminal_walks_the_wizard_and_scaffolds()
    {
        var (console, fs, runner, command) = Build();
        // Simulate a terminal (InputLines flips the console to interactive) and script the answers:
        // name → template select (2 = wasm) → --auth? no → --pwa? yes → --docker? no.
        console.InputLines = ["Spa", "2", "n", "y", "n"];

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.FileExists("/proj/Spa/Spa.csproj"));
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/index.html")); // wasm template
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/icon.svg"));   // --pwa answered yes
        Assert.False(fs.FileExists("/proj/Spa/Auth/CredentialStore.cs")); // --auth answered no
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
    }

    [Fact]
    public async Task No_name_without_a_terminal_still_hard_errors()
    {
        var (console, _, runner, command) = Build();
        // StringConsole defaults to redirected stdin (non-interactive) — the wizard must not run.
        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("name is required", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    private static (StringConsole Console, FakeFileSystem Fs, FakeProcessRunner Runner, NewCommand Command) Build()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner();
        return (console, fs, runner, new NewCommand(console, fs, runner, WorkingDirectory));
    }
}
