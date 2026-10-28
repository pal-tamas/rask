using System.Text.Json;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     Pins where <c>Rask.Core.targets</c> writes the scoped-TypeScript output, and what it never reads back
///     in, when the build's intermediate path is moved — BenchmarkDotNet's child build sets an ABSOLUTE one.
/// </summary>
/// <remarks>
///     A concatenated <c>$(MSBuildProjectDirectory)/$(IntermediateOutputPath)</c> nested that absolute path
///     under the project, so the compiled <c>.d.ts</c> landed in the source tree, the next build globbed it
///     back in as an ambient declaration, and tsgo refused to overwrite its own input (TS5055). Evaluation
///     only (<c>-getProperty</c>/<c>-getItem</c>), so nothing is downloaded and no compiler runs.
/// </remarks>
public sealed class ScopedTypeScriptOutputPathTests
{
    private static readonly string _targets =
        Path.Combine(PinnedTools.RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets");

    [Fact]
    public async Task An_absolute_intermediate_path_is_used_as_it_is_not_nested_under_the_project()
    {
        var project = Directory.CreateTempSubdirectory("rask-ts-objpath-");
        var obj = Directory.CreateTempSubdirectory("rask-ts-objpath-obj-");
        try
        {
            var probe = WriteProbe(project.FullName);

            var json = await Evaluate(probe, $"\"-p:IntermediateOutputPath={obj.FullName}/\"",
                "-getProperty:_RaskScopedTsOutDir -getProperty:_RaskScopedTsTypesDir -getProperty:_RaskScopedTsOptionsStamp");

            var properties = json.RootElement.GetProperty("Properties");
            foreach (var name in new[] { "_RaskScopedTsOutDir", "_RaskScopedTsTypesDir", "_RaskScopedTsOptionsStamp" })
            {
                var path = Path.GetFullPath(properties.GetProperty(name).GetString()!);
                Assert.StartsWith(obj.FullName, path, StringComparison.Ordinal);
                Assert.DoesNotContain(project.FullName, path, StringComparison.Ordinal);
            }
        }
        finally
        {
            project.Delete(recursive: true);
            obj.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_relative_intermediate_path_still_resolves_under_the_project()
    {
        var project = Directory.CreateTempSubdirectory("rask-ts-objpath-rel-");
        try
        {
            var probe = WriteProbe(project.FullName);

            var json = await Evaluate(probe, "-p:IntermediateOutputPath=obj/Debug/net10.0/",
                "-getProperty:_RaskScopedTsOutDir -getProperty:_RaskScopedTsTypesDir");

            var properties = json.RootElement.GetProperty("Properties");
            var obj = Path.Combine(project.FullName, "obj", "Debug", "net10.0", "rask");
            Assert.Equal(Path.Combine(obj, "ts") + Path.DirectorySeparatorChar, properties.GetProperty("_RaskScopedTsOutDir").GetString());
            Assert.Equal(Path.Combine(obj, "types") + Path.DirectorySeparatorChar, properties.GetProperty("_RaskScopedTsTypesDir").GetString());
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Build_output_anywhere_in_the_tree_is_never_a_scoped_source()
    {
        var project = Directory.CreateTempSubdirectory("rask-ts-strays-");
        try
        {
            var probe = WriteProbe(project.FullName);
            Touch(project.FullName, "Widget.ts");
            Touch(project.FullName, "Types", "lib.d.ts");
            Touch(project.FullName, "Users", "me", "Bench-1", "obj", "Rask.Ui", "rask", "ts", "UiEditor.d.ts");
            Touch(project.FullName, "Users", "me", "Bench-1", "obj", "Rask.Ui", "rask", "ts", "UiEditor.ts");
            Touch(project.FullName, "artifacts", "obj", "UiEditor.d.ts");
            Touch(project.FullName, "moved-obj", "rask", "ts", "Stale.d.ts");
            Touch(project.FullName, "moved-bin", "Stale.ts");

            var json = await Evaluate(probe,
                "-p:BaseIntermediateOutputPath=moved-obj/ -p:IntermediateOutputPath=moved-obj/ -p:BaseOutputPath=moved-bin/ -p:OutputPath=moved-bin/",
                "-getItem:_RaskScopedTs -getItem:_RaskScopedTsTypes");

            var items = json.RootElement.GetProperty("Items");
            Assert.Equal(["Widget.ts"], Identities(items, "_RaskScopedTs"));
            Assert.Equal([Path.Combine("Types", "lib.d.ts")], Identities(items, "_RaskScopedTsTypes").Where(i => !i.EndsWith("rask-globals.d.ts", StringComparison.Ordinal)));
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    private static string WriteProbe(string directory)
    {
        var probe = Path.Combine(directory, "probe.proj");
        File.WriteAllText(probe, $"""<Project><Import Project="{_targets}"/></Project>""");
        return probe;
    }

    private static void Touch(string root, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "export {};\n");
    }

    private static string[] Identities(JsonElement items, string name) =>
        [.. items.GetProperty(name).EnumerateArray()
            .Select(i => i.GetProperty("Identity").GetString()!.Replace('\\', Path.DirectorySeparatorChar))
            .Order(StringComparer.Ordinal)];

    private static async Task<JsonDocument> Evaluate(string probe, string properties, string queries)
    {
        var (exitCode, output) = await PinnedTools.Run(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            $"msbuild \"{probe}\" {properties} {queries}");

        Assert.True(exitCode == 0, output);
        return JsonDocument.Parse(output);
    }
}
