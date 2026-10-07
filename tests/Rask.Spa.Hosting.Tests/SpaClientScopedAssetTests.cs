using System.Diagnostics;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     A SPA host whose batteries bring Rask.Core also gets Rask.Core's scoped CSS and TypeScript globs, and
///     those must not reach into the front end: <c>client/src/App.css</c> and <c>client/vite.config.ts</c>
///     pair with no component and fail the build on RASK015/017. <c>rask new --template react --data</c>
///     did exactly that once the build hooks started flowing transitively.
/// </summary>
public sealed class SpaClientScopedAssetTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rask-spa-scoped-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public void The_front_ends_own_stylesheets_and_config_are_not_scoped_assets()
    {
        Write("client/package.json", "{}");
        Write("client/src/App.css", "a {}");
        Write("client/vite.config.ts", "export {}");
        Write("Features/HomePage.css", "a {}");

        var output = Evaluate();

        Assert.Contains("Features/HomePage.css", output, StringComparison.Ordinal);
        Assert.DoesNotContain("client/src/App.css", output, StringComparison.Ordinal);
        Assert.DoesNotContain("vite.config.ts", output, StringComparison.Ordinal);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Evaluate()
    {
        File.WriteAllText(Path.Combine(_dir, "Host.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="{Path.Combine(SrcDir, "Rask.Core", "build", "Rask.Core.targets")}"/>
              <Import Project="{Path.Combine(SrcDir, "Rask.Spa.Hosting", "build", "Rask.Spa.Hosting.targets")}"/>
            </Project>
            """);

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "msbuild", "Host.csproj", "-nologo", "-nodeReuse:false",
                     "-getItem:AdditionalFiles", "-getItem:_RaskScopedTs",
                 })
        {
            psi.ArgumentList.Add(argument);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"evaluation failed:\n{stdout}{stderr}");
        return stdout.Replace('\\', '/');
    }

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Spa.Hosting")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
