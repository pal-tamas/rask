using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

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
    public async Task A_bare_new_scaffolds_the_app_db_context()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Blog"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        Assert.True(fs.FileExists("/proj/Blog/Blog.csproj"));
        Assert.True(fs.FileExists("/proj/Blog/Features/Shared/AppDbContext.cs")); // --data
        Assert.Contains("AddDbContextFactory<AppDbContext>", fs.ReadAllText("/proj/Blog/Program.cs"), StringComparison.Ordinal);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
    }

    [Fact]
    public async Task Turning_off_a_battery_the_template_never_had_is_a_usage_error()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Spa", "--template", "wasm", "--no-data"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("nothing to change for: --no-data", console.ErrorText, StringComparison.Ordinal);
        // And it says what this template does have, so the next command line can be right.
        Assert.Contains("pwa", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasm")]
    public async Task The_browser_template_scaffolds_its_stylesheet(string template)
    {
        var (_, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(["Spa", "--template", template], CancellationToken.None);

        // It has to WRITE something — exit 0 alone is what the #838 bug already looked like, when both
        // generators collapsed styling to a bool and scaffolded plain CSS while reporting success.
        Assert.Equal(0, exit);

        const string stylesheet = "/proj/Spa/Styles/app.css";
        Assert.True(fs.FileExists(stylesheet), $"[{template}] scaffolded no {stylesheet}.");
    }

    // Both flags named a choice that no longer exists. Refused rather than ignored: a flag the CLI accepts
    // and then disregards is the most expensive kind to discover, and someone's muscle memory still has
    // these in it.
    [Theory]
    [InlineData("--tailwind", "Tailwind is built in")]
    [InlineData("--bootstrap", "Rask.Bootstrap has been removed")]
    public async Task The_styling_flags_are_refused_rather_than_ignored(string flag, string because)
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Shop", flag], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains(because, console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wasm_template_is_generated_directly_without_dotnet_new()
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Spa", "--template", "wasm"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(console.ErrorText);
        Assert.True(fs.FileExists("/proj/Spa/Spa.csproj"));
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/index.html"));
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/icon.svg")); // the PWA is on by default
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
    public async Task Missing_name_fails_without_running_dotnet()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("name is required", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unquoted_multi_word_name_is_refused_and_the_joined_name_suggested()
    {
        // `rask new My App` is the mistake this guards. Taking the first positional and dropping the rest
        // scaffolded a project called "My" and said nothing — silent, wrong, and only visible once the
        // files were on disk. Every sibling command already rejected a stray positional.
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["My", "App"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.False(fs.FileExists("/proj/My/My.csproj"));
        Assert.Contains("takes one project name", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("did you mean 'MyApp'?", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_different_names_are_refused_rather_than_one_silently_winning()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Pos", "--name", "Named"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Two different project names", console.ErrorText, StringComparison.Ordinal);

        // The same name twice is agreement, not a conflict.
        var (_, fs, _, ok) = Build();
        Assert.Equal(0, await ok.ExecuteAsync(["Shop", "--name", "Shop", "--no-restore", "--no-git"], CancellationToken.None));
        Assert.True(fs.FileExists("/proj/Shop/Shop.csproj"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_output_is_refused_rather_than_scaffolding_into_the_current_directory(string output)
    {
        // An empty --output resolved to the working directory, so the project was written into wherever
        // the user was standing instead of a folder of its own — with no error and nothing to notice.
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Shop", "--output", output], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.False(fs.FileExists("/proj/Shop.csproj"));
        Assert.Contains("--output needs a directory", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_output_naming_an_existing_file_fails_before_any_work_starts()
    {
        // This used to get as far as printing "Creating …" before the first write threw, and the resulting
        // "The file '…' already exists." never said what the command wanted with it.
        var (console, fs, runner, command) = Build();
        fs.Seed("/proj/notadir", "i am a file");

        var exit = await command.ExecuteAsync(["Shop", "--output", "notadir"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.DoesNotContain("Creating", console.OutText, StringComparison.Ordinal);
        Assert.Contains("is a file", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_template_fails()
    {
        var (console, _, runner, command) = Build();

        // Not a near-miss of a real template on purpose: 'svelte' used to stand in for "unknown" here,
        // and became a template, which turned this into a test of nothing.
        var exit = await command.ExecuteAsync(["MyApp", "--template", "cobol"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Option '--template' does not accept 'cobol'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Choose one of: server, wasm, react, preact, vue, angular, solid, svelte, lit, "
            + "nuxt, nextjs, sveltekit, solidstart, tanstack-start, analog.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_flag_for_template_fails_with_guidance()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--template", "wasm", "--no-cqrs"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("nothing to change for: --no-cqrs", console.ErrorText, StringComparison.Ordinal);
        // localization is no longer listed, because it is no longer a flag (#854). Listing it here would
        // name something the user cannot then pass.
        Assert.Contains("It supports: auth, docker, pwa.", console.ErrorText, StringComparison.Ordinal);
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

        var exit = await command.ExecuteAsync(["MyApp", "--no-restore", "--no-git"], CancellationToken.None);

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

        // Typing/pressing flips the console to interactive. The flow, in order:
        //   name → project type (down = wasm) → auth? no
        //   → batteries, offered PRE-TICKED as [pwa, docker]: down to docker, space to UNTICK it, enter.
        // One keypress shorter than it was: the styling question is gone, because Tailwind is built in.
        console.Type("Spa")
            .Press(ConsoleKey.DownArrow, ConsoleKey.Enter)
            .Type("n")
            .Press(ConsoleKey.DownArrow, ConsoleKey.Spacebar, ConsoleKey.Enter);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.FileExists("/proj/Spa/Spa.csproj"));
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/index.html")); // wasm template
        Assert.True(fs.FileExists("/proj/Spa/wwwroot/icon.svg"));   // the PWA was left ticked
        Assert.False(fs.FileExists("/proj/Spa/Features/Auth/Auth.cs")); // auth answered no
        Assert.False(fs.FileExists("/proj/Spa/Dockerfile"));            // docker unticked
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("restore"));
    }

    /// <summary>
    /// The other half of the pre-ticked checklist: pressing enter through it keeps everything, so the
    /// wizard's fastest path and a bare <c>rask new</c> produce the same project.
    /// </summary>
    [Fact]
    public async Task Accepting_the_wizards_defaults_keeps_every_battery()
    {
        var (console, fs, _, command) = Build();

        // name → project type (enter = server) → styling (enter = plain) → auth? no → batteries (enter).
        console.Type("Shop")
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Type("n")
            .Press(ConsoleKey.Enter);

        var exit = await command.ExecuteAsync(["--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.ReadAllText("/proj/Shop/Program.cs");
        Assert.Contains("AddRaskJobs<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskDashboard<AppDbContext>", program, StringComparison.Ordinal);
        Assert.True(fs.FileExists("/proj/Shop/Dockerfile"));
        Assert.False(fs.FileExists("/proj/Shop/Features/Auth/CredentialStore.cs"));
    }

    /// <summary>
    /// The gap-filling contract, on the new flag shape: a <c>--no-*</c> already on the command line is an
    /// answer to the battery question, so the checklist is skipped rather than asked over the top of it.
    /// </summary>
    [Fact]
    public async Task A_no_flag_on_the_command_line_skips_the_battery_question()
    {
        var (console, fs, _, command) = Build();

        // name → project type (enter = server) → styling (enter = plain) → auth? no. No battery question.
        console.Type("Shop")
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Type("n");

        var exit = await command.ExecuteAsync(["--no-ops", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.ReadAllText("/proj/Shop/Program.cs");
        Assert.DoesNotContain("AddRaskDashboard", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskJobs<AppDbContext>", program, StringComparison.Ordinal);
    }

    /// <summary>The wizard asks nothing about styling, because there is nothing to ask.</summary>
    /// <remarks>
    ///     It used to be a three-answer question. Tailwind is built in now, so a question would have one
    ///     answer — and a prompt whose answer is fixed is a keystroke charged for nothing.
    /// </remarks>
    [Fact]
    public async Task The_wizard_does_not_ask_about_styling()
    {
        var (console, fs, _, command) = Build();

        // name → project type (enter = server, the default) → Dockerfile? no → batteries.
        console.Type("Spa")
            .Press(ConsoleKey.Enter)
            .Type("n")
            .Press(ConsoleKey.Enter);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(fs.FileExists("/proj/Spa/Program.cs")); // server template
        Assert.DoesNotContain("Styling", console.OutText, StringComparison.Ordinal);

        // And the project is styled anyway — the question going away must not take the stylesheet with it.
        Assert.True(fs.FileExists("/proj/Spa/Styles/app.css"));
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
    public async Task Database_is_no_longer_an_option()
    {
        // SQLite is the only database Rask wires, so there is nothing to choose. An unrecognised option is
        // a usage error rather than something quietly ignored — a flag that appears to be honoured but
        // isn't would leave someone believing they had scaffolded a different database.
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--database", "postgres"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--database", console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The headline of the whole design: a bare <c>rask new</c> is the full One Person Framework stack.
    /// </summary>
    [Fact]
    public async Task A_bare_new_wires_every_pillar()
    {
        var (_, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.ReadAllText("/proj/MyApp/Program.cs");
        Assert.Contains("UseRaskSqlite", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskSqliteSnapshots", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskJobs<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskMail<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskCache<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskOutbox<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskDashboard<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskLogging", program, StringComparison.Ordinal);
        Assert.True(fs.FileExists("/proj/MyApp/Dockerfile"));
        Assert.True(fs.FileExists("/proj/MyApp/wwwroot/icon.svg"));

        // …and auth is the one thing it does not decide for you.
        Assert.False(fs.FileExists("/proj/MyApp/Features/Auth/CredentialStore.cs"));
    }

    [Fact]
    public async Task A_battery_turned_off_is_the_only_one_that_goes()
    {
        var (_, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--no-snapshots", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.ReadAllText("/proj/MyApp/Program.cs");
        Assert.DoesNotContain("AddRaskSqliteSnapshots", program, StringComparison.Ordinal);
        Assert.Contains("UseRaskSqlite", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskJobs<AppDbContext>", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_the_database_off_takes_every_pillar_with_it()
    {
        var (_, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--no-data", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        var program = fs.ReadAllText("/proj/MyApp/Program.cs");
        Assert.DoesNotContain("AddDbContextFactory<AppDbContext>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRaskJobs", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRaskDashboard", program, StringComparison.Ordinal);
        Assert.False(fs.FileExists("/proj/MyApp/Features/Shared/AppDbContext.cs"));

        // The log store keeps a database of its own, so it survives.
        Assert.Contains("AddRaskLogging", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// The positive battery flags were real in the last release and are all over the internet, so they get
    /// an answer that says what changed rather than the generic unknown-option suggestion.
    /// </summary>
    [Theory]
    [InlineData("--data", "--no-data")]
    [InlineData("--jobs", "--no-jobs")]
    [InlineData("--ops", "--no-ops")]
    public async Task A_retired_battery_flag_names_its_replacement(string retired, string replacement)
    {
        var (console, fs, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", retired], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.False(fs.FileExists("/proj/MyApp/MyApp.csproj"));
        Assert.Contains("on by default", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(replacement, console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The database-backed pillars are hosted services, and one that can't find its table stops the host —
    /// so an unmigrated app doesn't warn, it exits. <c>rask new</c> normally migrates for you; when it
    /// can't, it has to say so.
    /// </summary>
    [Fact]
    public async Task Skipping_the_restore_says_the_migration_still_has_to_happen()
    {
        var (console, _, _, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--no-restore"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("rask db add Init", console.OutText, StringComparison.Ordinal);
        Assert.Contains("rask db update", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_batteries_says_it_is_gone()
    {
        var (console, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["MyApp", "--all-batteries"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--all-batteries is gone", console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The language flags are gone (#854), and refused by name rather than quietly ignored.
    /// </summary>
    /// <remarks>
    ///     <c>--culture</c> took a VALUE, which is what makes ignoring it worse than usual: the tag would
    ///     be swallowed as a stray argument and the app scaffolded in English while the command line said
    ///     Hungarian. Nothing would report it. The message names Program.cs, because "where do I put my
    ///     languages now" is the only question a reader of this error has.
    /// </remarks>
    [Theory]
    [InlineData("--culture", "hu")]
    [InlineData("--culture=hu", null)]
    [InlineData("--no-localization", null)]
    [InlineData("--localization", null)]
    public async Task The_language_flags_are_refused_and_say_where_languages_live(string flag, string? value)
    {
        var (console, _, runner, command) = Build();

        string[] args = value is null ? ["MyApp", flag] : ["MyApp", flag, value];
        var exit = await command.ExecuteAsync(args, CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("Program.cs", console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shape the removed template's bug actually took: <c>--template native</c> was accepted, fell
    /// through to the default arm, wrote an ASP.NET server project and exited 0. TemplateCatalogTests pins
    /// the catalog entry's absence; this pins the end-to-end refusal, which is what a user would have hit.
    /// </summary>
    [Theory]
    [InlineData("native")]
    [InlineData("wasm-hosted")]
    public async Task The_removed_template_is_a_usage_error_and_scaffolds_nothing(string removed)
    {
        var (console, fs, _, command) = Build();

        var exit = await command.ExecuteAsync(["Field", "--template", removed], CancellationToken.None);

        Assert.Equal(2, exit);
        // It names the templates that do exist.
        Assert.Contains("server", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("wasm", console.ErrorText, StringComparison.Ordinal);
        // Nothing was written: the bug wrote a whole Server project before signing off.
        Assert.False(fs.FileExists("/proj/Field/Field.csproj"));
    }

    // The first migration builds the project to load the DbContext, and with the batteries on that
    // build defaulted to RaskSpaBuild/RaskMetaBuild=true — so scaffolding a front-end template ran the
    // bundler, or on the meta lane a full Nuxt/Next PRODUCTION build, behind a line that reads
    // "Creating the first migration…". MSBuild reads properties from the environment, so the overlay on
    // the dotnet-ef child is what turns it off without touching `rask db`'s argument surface.
    [Fact]
    public async Task The_first_migration_does_not_build_the_front_end()
    {
        var (_, _, runner, command) = Build();

        var exit = await command.ExecuteAsync(["Blog"], CancellationToken.None);

        Assert.Equal(0, exit);

        var ef = runner.Invocations
            .Where(i => i.Arguments.Contains("ef") && i.Arguments.Contains("migrations"))
            .ToList();
        Assert.NotEmpty(ef);

        foreach (var invocation in ef)
        {
            Assert.NotNull(invocation.Environment);
            Assert.Equal("false", invocation.Environment!["RaskSpaBuild"]);
            Assert.Equal("false", invocation.Environment!["RaskMetaBuild"]);
        }
    }

    private static (StringConsole Console, FakeFileSystem Fs, FakeProcessRunner Runner, NewCommand Command) Build()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner();
        return (console, fs, runner, new NewCommand(console, fs, runner, WorkingDirectory));
    }
}
