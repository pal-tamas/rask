using System.Diagnostics;
using System.Text.Json;

namespace Rask.Core.Tests.ScopedAssets;

/// <summary>
///     Proves that a scoped asset is in <c>@(Watch)</c>, which is what makes <c>rask dev</c>'s
///     "scoped .css/.ts apply live" true.
/// </summary>
/// <remarks>
///     <para>
///         <c>dotnet watch</c> builds its file list from <c>@(Compile)</c>, <c>@(EmbeddedResource)</c>,
///         the project file and Razor content. A scoped asset is a <c>None</c> item, so it reaches the
///         watcher only through <c>@(Watch)</c>, the documented extension point. The banner has claimed
///         these apply live since long before they did (issue #862); the wiring landed in #871 and this
///         is the guard that was missing — the reason the promise could drift that far is that nothing
///         ever asserted it.
///     </para>
///     <para>
///         By real MSBuild evaluation rather than by reading the targets as text. A
///         <c>Assert.Contains("&lt;Watch Include=", targets)</c> would pass on a line that is inside a
///         dead condition, spelled against an item group that is empty, or excluded by a glob — every
///         way this can actually break. What matters is the item list MSBuild ends up with, so that is
///         what is asserted, against a throwaway project that imports the real targets file.
///     </para>
///     <para>
///         Membership is asserted exactly, decoys included. A test that only checked the wanted files
///         were present would still pass if the globs were widened to everything under the project —
///         including <c>bin/</c> and <c>node_modules/</c>, which would make every build re-trigger the
///         watcher.
///     </para>
/// </remarks>
public sealed class ScopedAssetWatchTests : IDisposable
{
    private readonly string _project;

    public ScopedAssetWatchTests()
    {
        _project = Path.Combine(Path.GetTempPath(), "rask-watch-" + Guid.NewGuid().ToString("n"));

        Write("probe.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <Import Project="{TargetsPath().Replace("\\", "/")}"/>
            </Project>
            """);

        // What a component's scoped assets look like.
        Write("Features/Widget.css", "body { color: red; }");
        Write("Features/Widget.ts", "export const a = 1;");
        Write("Features/Widget.d.ts", "declare const ambient: number;");

        // The decoys. Every one of these sits under a directory the globs exclude, and watching any
        // of them would make a build's own output re-trigger the watcher.
        Write("bin/Debug/stale.css", "body {}");
        Write("obj/Debug/stale.ts", "export const stale = 1;");
        Write("wwwroot/site.css", "body {}");
        Write("node_modules/pkg/index.ts", "export const vendor = 1;");
    }

    [Fact]
    public void A_scoped_stylesheet_and_module_are_both_watched()
    {
        var watched = Watched();

        Assert.Contains("Features/Widget.css", watched);
        Assert.Contains("Features/Widget.ts", watched);
    }

    [Fact]
    public void An_ambient_declaration_is_watched_because_the_compile_consumes_it()
    {
        // A .d.ts is an Input to the scoped-TypeScript compile target and is handed to tsgo, so
        // editing one changes what the build produces. It was the one scoped input left out of the
        // watch list — found by writing this test, fixed in the same change.
        Assert.Contains("Features/Widget.d.ts", Watched());
    }

    [Fact]
    public void Nothing_under_a_build_output_or_a_package_directory_is_watched()
    {
        var watched = Watched();

        Assert.DoesNotContain(watched, path => path.StartsWith("bin/", StringComparison.Ordinal));
        Assert.DoesNotContain(watched, path => path.StartsWith("obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(watched, path => path.StartsWith("wwwroot/", StringComparison.Ordinal));
        Assert.DoesNotContain(watched, path => path.StartsWith("node_modules/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_stylesheet_opt_out_takes_the_stylesheet_out_of_the_watch_list()
    {
        // RaskScopedCssAutoInclude=false means the app collects its own stylesheets, so watching
        // them on its behalf would be wrong — and would fire on files the build ignores.
        var watched = Watched("-p:RaskScopedCssAutoInclude=false");

        Assert.DoesNotContain("Features/Widget.css", watched);
        Assert.Contains("Features/Widget.ts", watched);
    }

    [Fact]
    public void The_module_opt_out_takes_both_the_module_and_its_declarations_out()
    {
        var watched = Watched("-p:RaskScopedTsAutoInclude=false");

        Assert.DoesNotContain("Features/Widget.ts", watched);
        Assert.DoesNotContain("Features/Widget.d.ts", watched);
        Assert.Contains("Features/Widget.css", watched);
    }

    [Fact]
    public void The_packaged_globals_declaration_is_not_pushed_onto_an_app()
    {
        // rask-globals.d.ts ships inside the package and cannot change under an app, so watching it
        // would only add a file that never fires. This is what the placement of its <Watch> guards.
        Assert.DoesNotContain(
            Watched(), path => path.Contains("rask-globals", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_project, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    /// <summary>The <c>@(Watch)</c> item list MSBuild evaluates, as forward-slashed relative paths.</summary>
    private IReadOnlyList<string> Watched(params string[] properties)
    {
        // -getItem: evaluates and prints; it runs no target, so this costs an evaluation rather than
        // a build. nodeReuse off because a persisted node would hold this temp directory open and
        // outlive the test that made it.
        var arguments = new List<string>
        {
            "msbuild", Path.Combine(_project, "probe.csproj"), "-getItem:Watch", "-nologo", "-nodeReuse:false",
        };
        arguments.AddRange(properties);

        var (exitCode, output) = Run("dotnet", arguments);
        Assert.True(exitCode == 0, $"evaluating the probe project failed:\n{output}");

        using var json = JsonDocument.Parse(output);
        if (!json.RootElement.GetProperty("Items").TryGetProperty("Watch", out var items))
        {
            return [];
        }

        return [.. items.EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString() ?? string.Empty)
            .Select(identity => identity.Replace('\\', '/'))];
    }

    private static string TargetsPath() =>
        Path.Combine(RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_project, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static (int ExitCode, string Output) Run(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }
}
