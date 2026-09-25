using System.Diagnostics;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     The build tells the host where the compiled stylesheet is served, as the app assembly's
///     <c>Rask.Stylesheet</c> metadata, and RaskApp and the WASM host link it — so the App does not.
/// </summary>
public sealed class StylesheetMetadataTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "rask-stylesheet-meta-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* left behind on a locked file */ }
    }

    [Fact]
    public void A_project_with_a_stylesheet_announces_where_it_is_served()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Styles"));
        File.WriteAllText(Path.Combine(_dir, "Styles", "app.css"), "@import \"tailwindcss\";");

        var metadata = AssemblyMetadata();

        Assert.Contains("\"Identity\": \"Rask.Stylesheet\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"Value\": \"css/app.css\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_without_one_announces_nothing()
    {
        Directory.CreateDirectory(_dir);

        var metadata = AssemblyMetadata();

        Assert.DoesNotContain("Rask.Stylesheet", metadata, StringComparison.Ordinal);
    }

    private string AssemblyMetadata()
    {
        var build = Path.Combine(SrcDir, "Rask.Tailwind", "build");
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="{Path.Combine(build, "Rask.Tailwind.props")}"/>
              <Import Project="{Path.Combine(build, "Rask.Tailwind.targets")}"/>
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
                     "msbuild", "App.csproj", "-nologo", "-nodeReuse:false", "-restore",
                     "-t:GetAssemblyAttributes", "-getItem:AssemblyMetadata",
                 })
        {
            psi.ArgumentList.Add(argument);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"the build failed:\n{stdout}{stderr}");
        return stdout;
    }

    private static string SrcDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Rask.Tailwind")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.NotNull(dir);
            return Path.Combine(dir!, "src");
        }
    }
}
