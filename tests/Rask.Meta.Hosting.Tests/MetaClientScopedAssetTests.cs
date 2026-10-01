using Rask.TestFiles;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     A meta-framework host carries Rask.Core's scoped CSS and TypeScript globs through Rask.Server, and those
///     must not reach into the front end: <c>client/app/globals.css</c> and <c>client/next.config.ts</c> pair
///     with no component and fail the build on RASK015/017 — every meta template did, once it moved onto
///     <c>RaskApp</c> (#1147).
/// </summary>
public sealed class MetaClientScopedAssetTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rask-meta-scoped-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public async Task The_front_ends_own_stylesheets_and_config_are_not_scoped_assets()
    {
        Write("client/package.json", "{}");
        Write("client/app/globals.css", "a {}");
        Write("client/next.config.ts", "export {}");
        Write("Features/HomePage.css", "a {}");

        var output = await Evaluate();

        Assert.Contains("Features/HomePage.css", output, StringComparison.Ordinal);
        Assert.DoesNotContain("client/app/globals.css", output, StringComparison.Ordinal);
        Assert.DoesNotContain("next.config.ts", output, StringComparison.Ordinal);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private async Task<string> Evaluate()
    {
        File.WriteAllText(Path.Combine(_dir, "Host.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RaskMetaFramework>nextjs</RaskMetaFramework>
              </PropertyGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.props")}"/>
              <Import Project="{Path.Combine(SrcDir, "Rask.Core", "build", "Rask.Core.targets")}"/>
              <Import Project="{Path.Combine(SrcDir, "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.targets")}"/>
            </Project>
            """);

        var result = await TestProcess.Run(
            "dotnet",
            ["msbuild", "Host.csproj", "-nologo", "-nodeReuse:false", "-getItem:AdditionalFiles", "-getItem:_RaskScopedTs"],
            _dir,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, $"evaluation failed:\n{result.Output}");
        return result.StandardOutput.Replace('\\', '/');
    }

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Meta.Hosting")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
