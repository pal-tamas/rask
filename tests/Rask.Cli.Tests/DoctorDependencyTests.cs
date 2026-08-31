using System.ComponentModel;
using System.Text.Json;
using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
///     The seven dependencies <c>rask doctor</c> reports on, each probed independently (#883).
/// </summary>
/// <remarks>
///     <para>
///         `doctor` probed three of the seven things the CLI shells out to. The other four — the
///         `wasm-tools` workload, Node, npm, `git` and `ssh` — were each discovered by failure instead,
///         and the workload's failure is the least legible of the lot: NETSDK1147 reads like a broken
///         machine rather than a missing install.
///     </para>
///     <para>
///         Every probe here is driven through <c>CaptureByExecutable</c>. The old fake answered every
///         <c>CaptureAsync</c> with one fixed result, and since all six tools are asked
///         <c>--version</c>, a test could not tell them apart — a probe wired to the wrong executable
///         would have passed. Each case below makes exactly one tool missing or old and leaves the rest
///         healthy, so a row can only go yellow for its own reason.
///     </para>
/// </remarks>
public sealed class DoctorDependencyTests
{
    private const string ProjectDir = "/proj";

    /// <summary>A machine with everything, which each test then breaks in exactly one place.</summary>
    private static ProcessResult Healthy(string executable, IReadOnlyList<string> arguments) =>
        (executable, arguments.Count > 0 ? arguments[0] : "") switch
        {
            ("dotnet", "workload") => new ProcessResult(0, "Installed Workload Id\n---\nwasm-tools\n", ""),
            ("dotnet", _) => new ProcessResult(0, "10.0.302", ""),
            ("node", _) => new ProcessResult(0, "v24.20.0", ""),
            ("npm", _) => new ProcessResult(0, "11.19.0", ""),
            ("git", _) => new ProcessResult(0, "git version 2.50.1", ""),
            // OpenSSH prints its banner to STDERR and leaves stdout empty. This is the shape that made
            // the shared stdout-only helper unusable for ssh.
            ("ssh", _) => new ProcessResult(0, "", "OpenSSH_9.8p1, LibreSSL 3.3.6"),
            ("docker", _) => new ProcessResult(0, "Docker version 27.3.1", ""),
            _ => new ProcessResult(0, "", ""),
        };

    private static (StringConsole Console, DoctorCommand Command) Build(
        Func<string, IReadOnlyList<string>, ProcessResult>? probe = null)
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed($"{ProjectDir}/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        var process = new FakeProcessRunner { CaptureByExecutable = probe ?? Healthy };
        return (console, new DoctorCommand(console, fs, process, ProjectDir));
    }

    /// <summary>Replaces one tool's answer, leaving every other tool healthy.</summary>
    private static Func<string, IReadOnlyList<string>, ProcessResult> Except(
        string executable, Func<ProcessResult> answer) =>
        (exe, args) => exe == executable ? answer() : Healthy(exe, args);

    /// <summary>What the real runner does when a binary is not on PATH: it throws.</summary>
    private static ProcessResult NotOnPath() => throw new Win32Exception("not found");

    [Fact]
    public async Task Every_dependency_the_CLI_shells_out_to_has_a_row()
    {
        var (console, command) = Build();

        await command.ExecuteAsync(["--json"], CancellationToken.None);

        var names = Names(console.OutText);

        // The three that existed, and the four that were discovered by failure.
        Assert.Contains("dotnet sdk", names);
        Assert.Contains("dotnet-ef", names);
        Assert.Contains("docker", names);
        Assert.Contains("wasm-tools", names);
        Assert.Contains("node", names);
        Assert.Contains("npm", names);
        Assert.Contains("git", names);
        Assert.Contains("ssh", names);
    }

    [Fact]
    public async Task A_healthy_machine_reports_no_warning_on_any_of_them()
    {
        // The negative control. Without it every assertion below could pass on a doctor that warns
        // unconditionally.
        var (console, command) = Build();

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        foreach (var name in new[] { "wasm-tools", "node", "npm", "git", "ssh", "docker" })
        {
            Assert.Equal("Ok", StatusOf(console.OutText, name));
        }
    }

    [Fact]
    public async Task A_missing_wasm_tools_workload_is_named_before_a_build_hits_NETSDK1147()
    {
        var (console, command) = Build(Except("dotnet", () => new ProcessResult(0, "10.0.302", "")));

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        // Warn, not Fail: a server app never needs it.
        Assert.Equal(0, exit);
        Assert.Equal("Warn", StatusOf(console.OutText, "wasm-tools"));
        Assert.Contains("dotnet workload install wasm-tools", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_workload_row_is_not_fooled_by_the_word_appearing_in_the_table()
    {
        // `dotnet workload list` prints a header and trailing prose that mention workloads generally.
        // A substring test over that output reports wasm-tools as installed on a machine with none.
        var (console, command) = Build(Except("dotnet", () => new ProcessResult(
            0,
            "Installed Workload Id      Manifest Version\n"
            + "--------------------------------------------\n"
            + "Use `dotnet workload search wasm-tools` to find more.\n",
            "")));

        await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal("Warn", StatusOf(console.OutText, "wasm-tools"));
    }

    [Fact]
    public async Task A_missing_node_is_reported_against_the_templates_that_need_it()
    {
        var (console, command) = Build(Except("node", NotOnPath));

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("Warn", StatusOf(console.OutText, "node"));
    }

    [Fact]
    public async Task A_node_below_the_scaffold_line_is_reported_even_though_it_is_present()
    {
        // The #886 machine exactly: 24.14.0 is above RaskSpaMinimumNode and below Angular's floor, so
        // everything builds and `rask new --template angular` fails after creating the directory.
        var (console, command) = Build(Except("node", () => new ProcessResult(0, "v24.14.0", "")));

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("Warn", StatusOf(console.OutText, "node"));
        Assert.Contains("24.14.0", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_current_LTS_node_is_not_warned_about()
    {
        var (console, command) = Build(Except("node", () => new ProcessResult(0, "v24.20.0", "")));

        await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal("Ok", StatusOf(console.OutText, "node"));
    }

    [Fact]
    public async Task Ssh_is_found_even_though_it_answers_on_stderr()
    {
        // The regression this probe exists for: the shared helper reads stdout only, so a perfectly
        // good ssh reported as missing. `ssh -V` writes its banner to stderr and prints nothing else.
        var (console, command) = Build();

        await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal("Ok", StatusOf(console.OutText, "ssh"));
        Assert.Contains("OpenSSH", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_ssh_is_reported_against_deploy()
    {
        var (console, command) = Build(Except("ssh", NotOnPath));

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("Warn", StatusOf(console.OutText, "ssh"));
    }

    [Fact]
    public async Task A_missing_git_is_reported_against_the_first_commit()
    {
        var (console, command) = Build(Except("git", NotOnPath));

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("Warn", StatusOf(console.OutText, "git"));
    }

    [Fact]
    public async Task A_missing_npm_is_reported_against_the_client_dev_server()
    {
        var (console, command) = Build(Except("npm", NotOnPath));

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("Warn", StatusOf(console.OutText, "npm"));
    }

    [Fact]
    public async Task An_SDK_below_the_targeted_major_is_reported_instead_of_echoed()
    {
        // Presence was never the whole question: a .NET 9 box showed a green row and then failed at the
        // first build, because the row printed whatever string the tool returned.
        var (console, command) = Build((exe, args) => exe == "dotnet" && args.Count > 0 && args[0] != "workload"
            ? new ProcessResult(0, "9.0.404", "")
            : Healthy(exe, args));

        await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal("Warn", StatusOf(console.OutText, "dotnet sdk"));
    }

    [Fact]
    public async Task None_of_the_new_rows_can_stop_a_command_from_starting()
    {
        // Only `dotnet` is fatal to everything. Every dependency added here is needed by SOME commands,
        // so a machine missing all of them still runs `rask new --template server` — and doctor has to
        // keep exiting 0, or it becomes a gate rather than a report.
        var (console, command) = Build((exe, args) => exe switch
        {
            "dotnet" when args.Count > 0 && args[0] == "workload" => new ProcessResult(0, "", ""),
            "dotnet" => new ProcessResult(0, "10.0.302", ""),
            _ => NotOnPath(),
        });

        var exit = await command.ExecuteAsync(["--json"], CancellationToken.None);

        Assert.Equal(0, exit);
    }

    private static IReadOnlyList<string> Names(string json) =>
        [.. JsonDocument.Parse(json).RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("name").GetString() ?? "")];

    private static string StatusOf(string json, string name) =>
        JsonDocument.Parse(json).RootElement.GetProperty("checks").EnumerateArray()
            .Where(check => check.GetProperty("name").GetString() == name)
            .Select(check => check.GetProperty("status").GetString() ?? "")
            .FirstOrDefault() ?? $"<no row named {name}>";
}
