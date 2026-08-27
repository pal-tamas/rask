using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Islands.Tasks;

/// <summary>
///     Writes the bundler's inputs for the islands in this project: one entry module each, and the
///     Vite config that builds them into one chunk apiece.
/// </summary>
/// <remarks>
///     Runs before the bundler and after the front-end files are known. It writes only into the
///     intermediate directory — nothing lands in the author's source tree, so an island project has no
///     generated files to gitignore.
/// </remarks>
public sealed class WriteIslandBuildInputsTask : Task
{
    /// <summary>The island front-end files, each carrying an <c>IslandName</c> and <c>Runtime</c>.</summary>
    [Required]
    public ITaskItem[] Islands { get; set; } = [];

    /// <summary>Where the generated entry modules and the config are written.</summary>
    [Required]
    public string IntermediateDirectory { get; set; } = string.Empty;

    /// <summary>Where the vendored adapters were copied.</summary>
    [Required]
    public string AdapterDirectory { get; set; } = string.Empty;

    /// <summary>Where the built chunks land.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>The manifest file the client runtime fetches.</summary>
    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>The URL prefix the built chunks are served from.</summary>
    [Required]
    public string PublicBase { get; set; } = string.Empty;

    /// <summary>The generated Vite config, for the target that invokes the bundler.</summary>
    [Output]
    public string ConfigPath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var entryDirectory = Path.Combine(IntermediateDirectory, "entries");
        var islands = new List<IslandEntry>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Islands)
        {
            var source = item.GetMetadata("FullPath");
            var name = item.GetMetadata("IslandName");
            if (string.IsNullOrEmpty(name))
            {
                name = Path.GetFileNameWithoutExtension(source);
            }

            // The browser resolves a module by this name, so two islands sharing one would collide in
            // the manifest — silently, and differently depending on build order. The generator reports
            // the same collision as RASK057 against the C# declarations; this catches the case where
            // two front-end files collide before any class has claimed them.
            if (seen.TryGetValue(name, out var already))
            {
                Log.LogError(
                    $"Rask islands: '{source}' and '{already}' would both register as '{name}'. "
                    + "The island name is the key the browser resolves a module by, so it has to be "
                    + "unique. Rename one of the files.");
                return false;
            }

            seen[name] = source;

            var runtime = item.GetMetadata("Runtime");
            islands.Add(new IslandEntry
            {
                Name = name,
                Source = source,
                Runtime = string.IsNullOrEmpty(runtime) ? "react" : runtime,
            });
        }

        if (islands.Count == 0)
        {
            // Nothing to build. Not an error: the targets only call this when a front-end file was
            // found, but a project can lose its last island without the build becoming wrong.
            return true;
        }

        var written = 0;
        foreach (var island in islands)
        {
            var entry = Path.Combine(entryDirectory, island.Name + ".entry.ts");
            written += IslandBuildPlan.WriteIfDifferent(entry, IslandBuildPlan.EntryModule(island, AdapterDirectory))
                ? 1
                : 0;
        }

        ConfigPath = Path.Combine(IntermediateDirectory, "vite.islands.config.mjs");
        written += IslandBuildPlan.WriteIfDifferent(
            ConfigPath,
            IslandBuildPlan.ViteConfig(islands, entryDirectory, OutputDirectory, ManifestPath, PublicBase))
            ? 1
            : 0;

        Log.LogMessage(
            written > 0 ? MessageImportance.High : MessageImportance.Low,
            $"Rask islands: {islands.Count} island(s), {written} build input(s) written.");

        return true;
    }
}
