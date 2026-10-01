using Rask.TestFiles;

namespace Rask.Server.Tests.Build;

/// <summary>
///     Whether <c>Rask.Server.targets</c> turns the CQRS wire codec on — and with it the TypeScript contracts — by
///     what the project serves: a front end in <c>client/</c> dispatches over the wire, a server-rendered app never
///     does. Evaluation only, so no build and no node.
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
    public async Task A_typescript_front_end_in_client_turns_the_codec_on()
    {
        Write("client/package.json", "{}");

        var codec = await Evaluate();

        Assert.Equal("true", codec);
    }

    [Fact]
    public async Task A_server_rendered_app_keeps_the_codec_off()
    {
        Write("Program.cs", "// no front end of its own");

        var codec = await Evaluate();

        Assert.Equal("false", codec);
    }

    [Fact]
    public async Task A_meta_framework_host_is_not_taken_for_a_typescript_front_end()
    {
        Write("client/package.json", "{}");

        var codec = await Evaluate("<RaskMetaFramework>nuxt</RaskMetaFramework>");

        Assert.Equal("false", codec);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private async Task<string> Evaluate(string properties = "")
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

        var result = await TestProcess.Run(
            "dotnet",
            ["msbuild", "Host.csproj", "-nologo", "-nodeReuse:false", "-getProperty:RaskCqrsCodec"],
            _dir,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, $"evaluation failed:\n{result.Output}");
        return result.StandardOutput.Trim();
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
