using System.Diagnostics;

namespace Rask.Server.Tests.Build;

/// <summary>
///     Whether <c>Rask.Server.targets</c> turns the CQRS wire codec on by what the project serves: a WebAssembly
///     client in <c>Client/</c> dispatches over the wire, a server-rendered app never does. Evaluation only, so no
///     build.
/// </summary>
public sealed class CqrsCodecDefaultTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rask-codec-default-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public void A_webassembly_client_in_Client_turns_the_codec_on()
    {
        Write("Client/Program.cs", "// the browser half");

        var codec = Evaluate();

        Assert.Equal("true", codec);
    }

    [Fact]
    public void A_server_rendered_app_keeps_the_codec_off()
    {
        Write("Program.cs", "// no front end of its own");

        var codec = Evaluate();

        Assert.Equal("false", codec);
    }

    [Fact]
    public void A_package_json_in_client_does_not_turn_the_codec_on()
    {
        Write("client/package.json", "{}");

        var codec = Evaluate();

        Assert.Equal("false", codec);
    }

    [Fact]
    public void An_explicit_setting_wins_over_the_client_convention()
    {
        Write("Client/Program.cs", "// the browser half");

        var codec = Evaluate("<RaskCqrsCodec>false</RaskCqrsCodec>");

        Assert.Equal("false", codec);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Evaluate(string properties = "")
    {
        File.WriteAllText(Path.Combine(_dir, "Host.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                {properties}
              </PropertyGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Server", "build", "Rask.Server.targets")}"/>
            </Project>
            """);

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "msbuild", "Host.csproj", "-nologo", "-nodeReuse:false", "-getProperty:RaskCqrsCodec" })
        {
            psi.ArgumentList.Add(argument);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"evaluation failed:\n{stdout}{stderr}");
        return stdout.Trim();
    }

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Server")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
