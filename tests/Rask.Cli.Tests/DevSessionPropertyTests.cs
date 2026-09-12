using System.Diagnostics;
using System.Text.Json;
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
    private static readonly string[] Properties =
        ["RaskSpaBuild", "RaskMetaBuild", "RaskExternalDevServer", "RaskWasmDevBundle"];

    [Fact]
    public async Task A_dev_session_flips_every_package_property()
    {
        var values = await Evaluate($"-p:{DevCommand.DevSessionProperty}=true");

        Assert.Equal("false", values["RaskSpaBuild"]);
        Assert.Equal("false", values["RaskMetaBuild"]);
        Assert.Equal("true", values["RaskExternalDevServer"]);
        Assert.Equal("true", values["RaskWasmDevBundle"]);
    }

    [Fact]
    public async Task Without_one_every_package_keeps_its_production_default()
    {
        var values = await Evaluate();

        Assert.Equal("true", values["RaskSpaBuild"]);
        Assert.Equal("true", values["RaskMetaBuild"]);
        Assert.Equal("false", values["RaskExternalDevServer"]);
        Assert.Equal(string.Empty, values["RaskWasmDevBundle"]);
    }

    [Fact]
    public async Task An_explicit_value_beats_the_dev_session()
    {
        // How `rask dev --no-hot-reload` keeps a wasm-hosted app on its published bundle.
        var values = await Evaluate(
            $"-p:{DevCommand.DevSessionProperty}=true",
            "-p:RaskSpaBuild=true",
            "-p:RaskMetaBuild=true",
            "-p:RaskExternalDevServer=false",
            "-p:RaskWasmDevBundle=false");

        Assert.Equal("true", values["RaskSpaBuild"]);
        Assert.Equal("true", values["RaskMetaBuild"]);
        Assert.Equal("false", values["RaskExternalDevServer"]);
        Assert.Equal("false", values["RaskWasmDevBundle"]);
    }

    [Theory]
    [InlineData("src/Rask.Core/build/Rask.Core.targets")]
    [InlineData("src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.targets")]
    [InlineData("src/Rask.Meta.Hosting/build/Rask.Meta.Hosting.targets")]
    public void Each_host_lane_bakes_the_key_the_app_reads(string targets)
    {
        // Three files because the three kinds of app import different ones (a React or meta host never
        // imports Rask.Core.targets). The key is the runtime's constant, so a rename cannot leave one behind.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), targets));

        Assert.Contains($"<AssemblyMetadata Include=\"{EditorDevSession.MetadataKey}\" Value=\"true\"", text, StringComparison.Ordinal);
        Assert.Contains("'$(RaskDevSession)' == 'true'", text, StringComparison.Ordinal);
    }

    private static async Task<Dictionary<string, string>> Evaluate(params string[] extra)
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
                   <Import Project="{Path.Combine(root, "src", "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.props")}" />
                   <Import Project="{Path.Combine(root, "src", "Rask.External", "build", "Rask.External.props")}" />
                   <Import Project="{Path.Combine(root, "src", "Rask.Wasm.Hosting", "build", "Rask.Wasm.Hosting.targets")}" />
                 </Project>
                 """);

            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory.FullName,
            };
            info.ArgumentList.Add("msbuild");
            info.ArgumentList.Add(project);
            info.ArgumentList.Add("-nologo");
            foreach (var property in Properties)
            {
                info.ArgumentList.Add($"-getProperty:{property}");
            }

            foreach (var argument in extra)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"dotnet msbuild failed ({process.ExitCode}):{Environment.NewLine}{output}{error}");

            using var json = JsonDocument.Parse(output);
            return json.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
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
