using Rask.Cli.Commands;
using Rask.Hosting.Shared;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>RaskDevSession=true</c> — the one switch <c>rask dev</c> and the scaffolded VS Code build task both
///     pass — really does flip each package's own dev-time property, and an explicit value still wins.
/// </summary>
/// <remarks>
///     <para>
///         Evaluated by MSBuild itself against the shipped props and targets, rather than read back with a
///         regex. The whole claim is about evaluation ORDER — the dev-session line has to come before each
///         package's own default, or the default is already set by the time it is read — and only an
///         evaluation can show that. One <c>dotnet msbuild -getProperty</c> per case; no targets run.
///     </para>
/// </remarks>
public sealed class DevSessionPropertyTests
{
    [Fact]
    public async Task A_dev_session_flips_every_package_property()
    {
        var value = await EvaluateSpaBuild($"-p:{DevCommand.DevSessionProperty}=true");

        Assert.Equal("false", value);
    }

    [Fact]
    public async Task Without_one_every_package_keeps_its_production_default()
    {
        var value = await EvaluateSpaBuild();

        Assert.Equal("true", value);
    }

    [Fact]
    public async Task An_explicit_value_beats_the_dev_session()
    {
        // How `rask dev --no-hot-reload` keeps a WebAssembly client on its published bundle: RaskSpaBuild=true.
        var value = await EvaluateSpaBuild($"-p:{DevCommand.DevSessionProperty}=true", "-p:RaskSpaBuild=true");

        Assert.Equal("true", value);
    }

    [Fact]
    public void Every_host_bakes_the_key_the_app_reads()
    {
        // Rask.Core.targets reaches every app, a wasm-hosted server included. The key is the runtime's
        // constant, so a rename cannot leave it behind.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src/Rask.Core/build/Rask.Core.targets"));

        Assert.Contains($"<AssemblyMetadata Include=\"{EditorDevSession.MetadataKey}\" Value=\"true\"", text, StringComparison.Ordinal);
        Assert.Contains("'$(RaskDevSession)' == 'true'", text, StringComparison.Ordinal);
    }

    // One -getProperty prints the bare value; several print a JSON document.
    private static async Task<string> EvaluateSpaBuild(params string[] extra)
    {
        var root = RepoRoot();
        var directory = Directory.CreateTempSubdirectory("rask-devsession-");

        try
        {
            var project = Path.Combine(directory.FullName, "Probe.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                 <Project>
                   <Import Project="{Path.Combine(root, "src", "Rask.Spa.Hosting", "build", "Rask.Spa.Hosting.props")}" />
                 </Project>
                 """);

            var result = await TestProcess.Run(
                "dotnet",
                ["msbuild", project, "-nologo", "-getProperty:RaskSpaBuild", .. extra],
                directory.FullName);

            Assert.True(
                result.ExitCode == 0,
                $"dotnet msbuild failed ({result.ExitCode}):{Environment.NewLine}{result.Output}");

            return result.StandardOutput.Trim();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
