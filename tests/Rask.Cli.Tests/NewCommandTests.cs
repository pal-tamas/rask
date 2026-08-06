using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class NewCommandTests
{
    private const string WorkingDirectory = "/proj";

    /// <summary>
    /// A released CLI pins generated projects to itself. A dev/CI build stamps a MinVer prerelease that was
    /// never published, so it walks back to the release it came after — MinVer names a prerelease for the
    /// version it is heading towards, bumping the patch, so 0.19.1-alpha.N came after 0.19.0.
    ///
    /// <para>This was a hardcoded "0.17.0" that silently rotted two minor versions behind the repo. Derived,
    /// it can't.</para>
    /// </summary>
    [Theory]
    [InlineData("0.17.0", "0.17.0")]                 // a published stable pins exactly
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("0.18.0-alpha.0.5", "0.17.0")]       // dev build after v0.17.0
    [InlineData("0.19.1-alpha.0.5+abc123", "0.19.0")] // ...and after v0.19.0
    [InlineData("2.0.4-beta.1", "2.0.3")]
    [InlineData("0.0.0", "0.0.0")]                   // the no-version sentinel: nothing to derive from
    [InlineData("", "")]
    [InlineData("0.20.0-alpha.0.1", "0.19.0")]       // a minor bump: .0 of the previous minor was published
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.1")]   // nothing published under this major to walk back to
    public void ResolvePackageVersion_walks_a_prerelease_back_to_its_release(string cliVersion, string expected) =>
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
        Assert.True(fs.FileExists("/proj/MyApp/Features/Auth/CredentialStore.cs")); // --auth
        // It restores, and never shells to `dotnet new` / installs Rask.Templates.
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("new"));
    }

    [Fact]
    public async Task Data_flag_scaffolds_the_app_db_context()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Blog", "--data"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        Assert.True(fs.FileExists("/proj/Blog/Blog.csproj"));
        Assert.True(fs.FileExists("/proj/Blog/Features/Shared/AppDbContext.cs")); // --data
        Assert.Contains("AddDbContextFactory<AppDbContext>", fs.ReadAllText("/proj/Blog/Program.cs"), StringComparison.Ordinal);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
    }

    [Fact]
    public async Task Data_flag_is_rejected_on_the_wasm_template()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Spa", "--template", "wasm", "--data"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("does not support: --data", console.ErrorText, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("my-app")]    // a dash isn't valid in a namespace
    [InlineData("9Lives")]    // can't start with a digit
    [InlineData("class")]     // a reserved keyword
    [InlineData("Foo.")]      // trailing dot → empty segment
    [InlineData("Foo..Bar")]  // empty middle segment
    public async Task Invalid_project_name_is_rejected_before_writing_anything(string name)
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync([name], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations); // never even restored
        Assert.Contains("isn't a valid project name", console.ErrorText, StringComparison.Ordinal);
        Assert.False(fs.FileExists($"/proj/{name}/{name}.csproj"));
    }

    [Theory]
    [InlineData("Shop")]
    [InlineData("Contoso.Shop")] // a dotted name is a valid multi-part namespace
    public async Task Valid_project_name_including_dotted_is_accepted(string name)
    {
        var (console, fs, _, command) = Build();

        var exit = await command.ExecuteAsync([name], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        Assert.True(fs.FileExists($"/proj/{name}/{name}.csproj"));
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
        Assert.False(fs.FileExists("/proj/MobileApp/Features/Shared/App.cs")); // local-only
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
        Assert.True(fs.FileExists("/proj/MobileApp/Features/Shared/App.cs"));         // local shared shell
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

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Option '--host' does not accept 'cloud'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Choose one of: local, server.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_option_is_rejected_for_non_native_templates()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "server", "--host", "local"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
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
        Assert.True(fs.FileExists("/proj/HostedApp/HostedApp.Server/Features/Auth/CredentialStore.cs")); // --auth
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

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("name is required", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_template_fails()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "svelte"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Option '--template' does not accept 'svelte'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Choose one of: server, wasm, wasm-hosted, native.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_flag_for_template_fails_with_guidance()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "wasm", "--cqrs"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("does not support: --cqrs", console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard used to check only for the restore target, so scaffolding into a directory that already
    /// held a Program.cs or a Features/ tree overwrote it — silently, with no --force to consent to and
    /// nothing to undo it.
    /// </summary>
    [Fact]
    public async Task Scaffolding_over_existing_files_is_refused_without_force()
    {
        var (console, fs, runner, command) = Build();
        fs.Seed("/proj/MyApp/Program.cs", "// something the user wrote");

        var exit = await command.ExecuteAsync(["MyApp"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Program.cs", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("--force", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal("// something the user wrote", fs.Files[Path.GetFullPath("/proj/MyApp/Program.cs")]);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Force_scaffolds_over_them()
    {
        var (_, fs, _, command) = Build();
        fs.Seed("/proj/MyApp/Program.cs", "// something the user wrote");

        var exit = await command.ExecuteAsync(["MyApp", "--force"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("something the user wrote", fs.Files[Path.GetFullPath("/proj/MyApp/Program.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_restore_is_reported_as_a_failure()
    {
        // The files are written and correct, but the project won't build — and `rask new && dotnet build`
        // would otherwise step straight past it.
        var (console, _, runner, command) = Build();
        runner.RunExitCode = 1;

        var exit = await command.ExecuteAsync(["MyApp"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("restoring its packages failed", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_restore_skips_it_and_succeeds()
    {
        var (console, _, runner, command) = Build();
        runner.RunExitCode = 1; // would fail if it ran

        var exit = await command.ExecuteAsync(["MyApp", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Skipped restore", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_option_fails()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--frobnicate"], CancellationToken.None);

        // 2, not 1: "you typed something wrong" is a different outcome from "what you asked for failed",
        // and a script driving the CLI should be able to tell them apart.
        Assert.Equal(CliCommand.UsageExitCode, exit);
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
        Assert.False(fs.FileExists("/proj/Spa/Features/Auth/Auth.cs")); // --auth answered no
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
    }

    [Fact]
    public async Task Dry_run_prints_the_plan_and_writes_nothing()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "server", "--auth", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("would write", console.OutText, StringComparison.Ordinal);
        Assert.Contains("MyApp.csproj", console.OutText, StringComparison.Ordinal);
        // Nothing is written and nothing is restored.
        Assert.False(fs.FileExists("/proj/MyApp/MyApp.csproj"));
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task No_name_without_a_terminal_still_hard_errors()
    {
        var (console, _, runner, command) = Build();
        // StringConsole defaults to redirected stdin (non-interactive) — the wizard must not run.
        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("name is required", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Unknown_database_fails_and_lists_the_available_ones()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--data", "--database", "mongo"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Option '--database' does not accept 'mongo'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("sqlite, postgres", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    [InlineData("native")]
    public async Task Database_is_rejected_on_a_template_that_has_no_database(string template)
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(
            ["MyApp", "--template", template, "--database", "postgres"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("does not support --database", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshots_with_a_server_database_is_rejected_rather_than_dropped()
    {
        // Snapshots copy the database file. Silently ignoring the flag would leave someone believing they
        // had scheduled backups — the kind of thing found out after losing data, so it must fail loudly.
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(
            ["MyApp", "--data", "--database", "postgres", "--snapshots"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--snapshots needs a file-based database", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshots_with_sqlite_is_accepted()
    {
        var (_, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(
            ["MyApp", "--data", "--database", "sqlite", "--snapshots", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("AddRaskSqliteSnapshots", fs.ReadAllText("/proj/MyApp/Program.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_batteries_with_a_server_database_succeeds_without_snapshots()
    {
        // The expansion narrows to what applies; only an explicitly named --snapshots is an error.
        var (_, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(
            ["MyApp", "--all-batteries", "--database", "postgres", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.ReadAllText("/proj/MyApp/Program.cs");
        Assert.Contains("UseRaskPostgres", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRaskSqliteSnapshots", program, StringComparison.Ordinal);
    }

    private static (StringConsole Console, FakeFileSystem Fs, FakeProcessRunner Runner, NewCommand Command) Build()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner();
        return (console, fs, runner, new NewCommand(console, fs, runner, WorkingDirectory));
    }
}
