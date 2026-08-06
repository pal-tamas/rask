using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class DbCommandTests
{
    private const string ProjectDir = "/proj";
    private const string Csproj = "/proj/App.csproj";

    // --- BuildEfArguments (pure) -------------------------------------------------------------------

    [Fact]
    public void Add_maps_to_migrations_add_with_project()
    {
        var args = DbCommand.BuildEfArguments("add", "InitialCreate", ProjectDir, ProjectDir, context: null, output: null, force: false, passthrough: []);

        Assert.Equal(["ef", "migrations", "add", "InitialCreate", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void Add_output_becomes_output_dir()
    {
        var args = DbCommand.BuildEfArguments("add", "Init", ProjectDir, ProjectDir, context: null, output: "Data/Migrations", force: false, passthrough: []);

        Assert.Equal(["ef", "migrations", "add", "Init", "--output-dir", "Data/Migrations", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void Remove_maps_to_migrations_remove()
    {
        var args = DbCommand.BuildEfArguments("remove", name: null, ProjectDir, ProjectDir, context: null, output: null, force: false, passthrough: []);

        Assert.Equal(["ef", "migrations", "remove", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void List_maps_to_migrations_list()
    {
        var args = DbCommand.BuildEfArguments("list", name: null, ProjectDir, ProjectDir, context: null, output: null, force: false, passthrough: []);

        Assert.Equal(["ef", "migrations", "list", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void Update_maps_to_database_update()
    {
        var args = DbCommand.BuildEfArguments("update", name: null, ProjectDir, ProjectDir, context: null, output: null, force: false, passthrough: []);

        Assert.Equal(["ef", "database", "update", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void Update_with_target_passes_the_target_migration()
    {
        var args = DbCommand.BuildEfArguments("update", "20240101_Init", ProjectDir, ProjectDir, context: null, output: null, force: false, passthrough: []);

        Assert.Equal(["ef", "database", "update", "20240101_Init", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void Drop_maps_to_database_drop_and_force_is_forwarded()
    {
        var args = DbCommand.BuildEfArguments("drop", name: null, ProjectDir, ProjectDir, context: null, output: null, force: true, passthrough: []);

        Assert.Equal(["ef", "database", "drop", "--force", "--project", ProjectDir, "--startup-project", ProjectDir], args);
    }

    [Fact]
    public void Context_and_startup_project_are_forwarded()
    {
        var args = DbCommand.BuildEfArguments("list", name: null, ProjectDir, "/host", context: "AppDbContext", output: null, force: false, passthrough: []);

        Assert.Equal(["ef", "migrations", "list", "--project", ProjectDir, "--startup-project", "/host", "--context", "AppDbContext"], args);
    }

    [Fact]
    public void Passthrough_is_appended_verbatim()
    {
        var args = DbCommand.BuildEfArguments("update", name: null, ProjectDir, ProjectDir, context: null, output: null, force: false, passthrough: ["--verbose"]);

        Assert.Equal(["ef", "database", "update", "--project", ProjectDir, "--startup-project", ProjectDir, "--verbose"], args);
    }

    // --- ExecuteAsync ------------------------------------------------------------------------------

    [Fact]
    public async Task Add_resolves_the_project_and_runs_dotnet_ef()
    {
        var (command, runner, _) = CreateWithProject();

        var exit = await command.ExecuteAsync(["add", "InitialCreate"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("dotnet", runner.LastRun!.FileName);
        Assert.Equal(["ef", "migrations", "add", "InitialCreate", "--project", ProjectDir, "--startup-project", ProjectDir], runner.LastRun.Arguments);
    }

    [Fact]
    public async Task Explicit_project_overrides_discovery()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var command = new DbCommand(console, new FakeFileSystem(), runner, ProjectDir);

        var exit = await command.ExecuteAsync(["list", "--project", "src/Api/Api.csproj"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(["ef", "migrations", "list", "--project", "src/Api/Api.csproj", "--startup-project", "src/Api/Api.csproj"], runner.LastRun!.Arguments);
    }

    [Fact]
    public async Task Missing_ef_tool_triggers_a_global_install()
    {
        var (command, runner, _) = CreateWithProject();
        runner.CaptureResult = new ProcessResult(1, string.Empty, string.Empty); // ef --version fails → not installed

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i is { Captured: false, Arguments: ["tool", "install", "--global", "dotnet-ef"] });
    }

    [Fact]
    public async Task Failed_ef_install_aborts_without_running_a_migration()
    {
        var (command, runner, console) = CreateWithProject();
        runner.CaptureResult = new ProcessResult(1, string.Empty, string.Empty); // not installed
        runner.RunExitCode = 1; // install fails

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Count > 0 && i.Arguments[0] == "ef" && i.Arguments.Contains("migrations"));
        Assert.Contains("dotnet tool install --global dotnet-ef", console.ErrorText);
    }

    [Fact]
    public async Task No_action_fails_with_guidance()
    {
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("add, remove, list, update, drop", console.ErrorText);
    }

    [Fact]
    public async Task Unknown_action_fails()
    {
        var (command, runner, _) = CreateWithProject();

        var exit = await command.ExecuteAsync(["migrate"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Add_without_a_name_fails()
    {
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync(["add"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("needs a migration name", console.ErrorText);
    }

    [Fact]
    public async Task Remove_with_a_name_fails()
    {
        var (command, runner, _) = CreateWithProject();

        var exit = await command.ExecuteAsync(["remove", "Oops"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Missing_project_fails_with_guidance()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var command = new DbCommand(console, new FakeFileSystem(), runner, ProjectDir);

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--project", console.ErrorText);
    }

    [Fact]
    public async Task Output_on_a_non_add_action_fails()
    {
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync(["list", "--output", "Migrations"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--output only applies", console.ErrorText);
    }

    [Fact]
    public async Task Yes_on_a_non_drop_action_fails()
    {
        // Renamed from --force (#601): on new/generate that word means "overwrite files", here it meant
        // "skip the confirmation" — and the one that destroys a database was reachable by muscle memory
        // from the one that overwrites a file.
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync(["update", "--yes"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--yes only applies", console.ErrorText);
    }

    [Fact]
    public async Task The_old_force_spelling_is_rejected_rather_than_silently_ignored()
    {
        // A script carrying `rask db drop --force` must not quietly lose its confirmation skip and hang
        // on a prompt — or, worse, be taken as an unknown option that some future flag reclaims.
        var (command, runner, console) = CreateWithProject();

        var exit = await command.ExecuteAsync(["drop", "--force"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--force", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("backup", new string[0])]
    [InlineData("restore", new[] { "out.db" })]
    public async Task File_copy_subcommands_refuse_a_client_server_database(string subcommand, string[] rest)
    {
        // Both copy a database *file*. A "backup" that quietly did nothing is the worst outcome a backup
        // command has, so this refuses and names the tool that does work instead.
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var fileSystem = new FakeFileSystem();
        fileSystem.Seed(Csproj, "<Project><ItemGroup><PackageReference Include=\"Rask.Postgres\" /></ItemGroup></Project>");
        var command = new DbCommand(console, fileSystem, runner, ProjectDir);

        var exit = await command.ExecuteAsync([subcommand, .. rest], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains($"`rask db {subcommand}` works on a SQLite file", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("pg_dump", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrations_work_the_same_on_a_client_server_database()
    {
        // Migrations forward to `dotnet ef`, which is provider-agnostic — the refusal above must not spread
        // to the commands that do work everywhere.
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var fileSystem = new FakeFileSystem();
        fileSystem.Seed(Csproj, "<Project><ItemGroup><PackageReference Include=\"Rask.Postgres\" /><PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" /></ItemGroup></Project>");
        var command = new DbCommand(console, fileSystem, runner, ProjectDir);

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(["ef", "migrations", "list", "--project", ProjectDir, "--startup-project", ProjectDir], runner.LastRun!.Arguments);
    }

    [Fact]
    public async Task Adds_ef_design_when_the_startup_project_lacks_it()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var fileSystem = new FakeFileSystem();
        fileSystem.Seed(Csproj, "<Project><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" /></ItemGroup></Project>");
        var command = new DbCommand(console, fileSystem, runner, ProjectDir);

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments is ["add", Csproj, "package", "Microsoft.EntityFrameworkCore.Design"]);
        // Adding never blocks — the ef command still runs afterwards.
        Assert.Equal(["ef", "migrations", "list", "--project", ProjectDir, "--startup-project", ProjectDir], runner.LastRun!.Arguments);
    }

    [Fact]
    public async Task Does_not_add_ef_design_when_already_referenced()
    {
        var (command, runner, _) = CreateWithProject(); // seeded csproj already references EF Core Design

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("Microsoft.EntityFrameworkCore.Design"));
    }

    [Fact]
    public async Task Failed_ef_design_add_surfaces_the_manual_command_but_still_runs_ef()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner { RunExitCode = 1 }; // the `dotnet add package` fails
        var fileSystem = new FakeFileSystem();
        fileSystem.Seed(Csproj, "<Project></Project>");
        var command = new DbCommand(console, fileSystem, runner, ProjectDir);

        var exit = await command.ExecuteAsync(["list"], CancellationToken.None);

        Assert.Contains("dotnet add", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal(["ef", "migrations", "list", "--project", ProjectDir, "--startup-project", ProjectDir], runner.LastRun!.Arguments);
    }

    private static (DbCommand Command, FakeProcessRunner Runner, StringConsole Console) CreateWithProject()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var fileSystem = new FakeFileSystem();
        // Seed a csproj that already references EF Core Design so the common execute paths don't trigger
        // the auto-add; the add behavior has its own dedicated tests.
        fileSystem.Seed(Csproj, "<Project><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" /></ItemGroup></Project>");
        return (new DbCommand(console, fileSystem, runner, ProjectDir), runner, console);
    }
}
