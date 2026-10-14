using System.Diagnostics;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     The front end's install and build are incremental for every package manager and every layout: a
///     source edit rebuilds the bundle, and a build with nothing changed runs neither step (#1237).
/// </summary>
/// <remarks>
///     <para>
///         A yarn client at the repository root was built once and then served stale for ever: its sources
///         lived under <c>apps/</c>, outside the fixed <c>src/**</c> globs, and the install's up-to-date check
///         compared against npm's <c>node_modules/.package-lock.json</c>, which yarn never writes, so the
///         install ran on every build instead.
///     </para>
///     <para>
///         These drive the SHIPPED <c>build/Rask.Spa.Hosting.props</c> and <c>.targets</c> through a real
///         MSBuild. The install and build commands are small node scripts that count their runs in a log
///         outside the client, so the counts are what MSBuild's up-to-date checks decided.
///     </para>
/// </remarks>
public sealed class ClientIncrementalBuildTests : IDisposable
{
    private const string InstallScript = """
        const fs = require("fs");
        fs.mkdirSync("node_modules", { recursive: true });
        fs.appendFileSync(process.argv[2], "install\n");
        """;

    private const string BuildScript = """
        const fs = require("fs");
        fs.mkdirSync("dist", { recursive: true });
        fs.writeFileSync("dist/index.html", "<!doctype html>");
        fs.appendFileSync(process.argv[2], "build\n");
        """;

    /// <summary>A repository-root client's globs; <c>%3B</c> because a bare <c>;</c> splits <c>-p:</c> into properties.</summary>
    private const string RootInputs = "*.json%3B*.js%3Bapps/**/*%3Blibs/**/*";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "rask-spa-incremental", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    [Fact]
    public async Task A_yarn_client_at_the_repository_root_rebuilds_when_a_source_outside_src_changes()
    {
        Assert.SkipWhen(!await NodeRuns(), "node is not on PATH, so the client build was never exercised.");
        var host = Client("repo", "yarn.lock", "apps/web/main.ts", "repo/apps/web/host");
        await Build(host, "../../..", RootInputs);

        await Build(host, "../../..", RootInputs);
        var unchanged = Runs();
        Touch("repo/apps/web/main.ts");
        await Build(host, "../../..", RootInputs);

        Assert.Equal(["install", "build"], unchanged);
        Assert.Equal(["install", "build", "build"], Runs());
    }

    [Fact]
    public async Task A_yarn_client_reinstalls_when_its_lockfile_changes_and_only_then()
    {
        Assert.SkipWhen(!await NodeRuns(), "node is not on PATH, so the client install was never exercised.");
        var host = Client("repo", "yarn.lock", "src/main.ts", "host");
        await Build(host, "../repo", inputs: null);

        Touch("repo/yarn.lock");
        await Build(host, "../repo", inputs: null);
        await Build(host, "../repo", inputs: null);

        Assert.Equal(["install", "build", "install"], Runs());
    }

    [Fact]
    public async Task A_deleted_node_modules_reinstalls_although_the_stamp_survives()
    {
        Assert.SkipWhen(!await NodeRuns(), "node is not on PATH, so the client install was never exercised.");
        var host = Client("repo", "yarn.lock", "src/main.ts", "host");
        await Build(host, "../repo", inputs: null);

        Directory.Delete(Path.Combine(_root, "repo", "node_modules"), recursive: true);
        await Build(host, "../repo", inputs: null);

        Assert.Equal(["install", "build", "install"], Runs());
    }

    [Fact]
    public async Task An_npm_client_rebuilds_on_a_src_edit_and_skips_both_steps_when_nothing_changed()
    {
        Assert.SkipWhen(!await NodeRuns(), "node is not on PATH, so the client build was never exercised.");
        var host = Client("repo", "package-lock.json", "src/main.ts", "host");
        await Build(host, "../repo", inputs: null);

        await Build(host, "../repo", inputs: null);
        var unchanged = Runs();
        Touch("repo/src/main.ts");
        await Build(host, "../repo", inputs: null);

        Assert.Equal(["install", "build"], unchanged);
        Assert.Equal(["install", "build", "build"], Runs());
    }

    [Theory]
    [InlineData("package-lock.json", null, "npm ci")]
    [InlineData("yarn.lock", null, "yarn install --frozen-lockfile")]
    [InlineData("pnpm-lock.yaml", null, "pnpm install --frozen-lockfile")]
    [InlineData(null, null, "npm install")]
    [InlineData("yarn.lock", "yarn install --immutable", "yarn install --immutable")]
    public async Task The_install_command_follows_the_lockfile_unless_the_project_names_one(
        string? lockfile,
        string? configured,
        string expected)
    {
        var host = Client("repo", lockfile, "src/main.ts", "host");

        var command = await Evaluate(host, configured);

        Assert.Equal(expected, command);
    }

    /// <summary>A client in <paramref name="client" /> and an empty host directory, as the test names them.</summary>
    private string Client(string client, string? lockfile, string source, string host)
    {
        Write($"{client}/package.json", "{}");
        Write($"{client}/install.cjs", InstallScript);
        Write($"{client}/build.cjs", BuildScript);
        Write($"{client}/{source}", "export {}");
        if (lockfile is not null)
        {
            Write($"{client}/{lockfile}", string.Empty);
        }

        var dir = Path.Combine(_root, host);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task Build(string host, string clientDir, string? inputs)
    {
        var project = Project(host);
        var log = Path.Combine(_root, "runs.log");
        var clientInputs = inputs is null ? string.Empty : $" \"-p:RaskSpaClientInputs={inputs}\"";

        var (exit, output) = await Run(
            "dotnet",
            $"msbuild \"{project}\" -t:_RaskSpaBuildClient -nologo -nodeReuse:false -p:RaskEmitTypeScript=false "
            + $"\"-p:RaskSpaClientDir={clientDir}\" \"-p:RaskSpaInstallCommand=node install.cjs {log}\" "
            + $"\"-p:RaskSpaBuildCommand=node build.cjs {log}\"{clientInputs}");

        Assert.True(exit == 0, $"the client build failed.\n{output}");
        Settle();
    }

    /// <summary>
    ///     Dates every file an hour back, sources and stamps alike, so the next <see cref="Touch" /> is newer
    ///     than any stamp whatever the file system's timestamp resolution — and nothing else is.
    /// </summary>
    private void Settle()
    {
        var past = DateTime.UtcNow.AddHours(-1);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, past);
        }
    }

    private static async Task<string> Evaluate(string host, string? installCommand)
    {
        var project = Project(host);
        var configured = installCommand is null ? string.Empty : $" \"-p:RaskSpaInstallCommand={installCommand}\"";

        var (exit, output) = await Run(
            "dotnet",
            $"msbuild \"{project}\" -nologo -p:RaskSpaClientDir=../repo -getProperty:_RaskSpaInstallCmd{configured}");

        Assert.True(exit == 0, $"evaluation failed.\n{output}");
        return output.Trim();
    }

    private static string Project(string host)
    {
        var build = Path.Combine(RepoRoot(), "src", "Rask.Spa.Hosting", "build");
        var project = Path.Combine(host, "host.proj");

        // Written once: at the repository root the host sits inside the client's own globs, and a
        // rewritten project file would be a changed source.
        if (File.Exists(project))
        {
            return project;
        }

        File.WriteAllText(
            project,
            $"""
             <Project>
               <Import Project="{Path.Combine(build, "Rask.Spa.Hosting.props")}"/>
               <PropertyGroup><IntermediateOutputPath>obj/</IntermediateOutputPath></PropertyGroup>
               <Import Project="{Path.Combine(build, "Rask.Spa.Hosting.targets")}"/>
             </Project>
             """);
        return project;
    }

    private string[] Runs()
    {
        var log = Path.Combine(_root, "runs.log");
        return File.Exists(log) ? File.ReadAllLines(log) : [];
    }

    /// <summary>An edit: the file is now newer than every stamp <see cref="Settle" /> dated back.</summary>
    private void Touch(string path) =>
        File.SetLastWriteTimeUtc(Path.Combine(_root, path), DateTime.UtcNow);

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static async Task<bool> NodeRuns()
    {
        try
        {
            return (await Run("node", "--version")).Exit == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<(int Exit, string Output)> Run(string file, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string RepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
