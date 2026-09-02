using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Writes the bundler's inputs for the islands in this project: one entry module each, and the
///     Vite config that builds them into one chunk apiece.
/// </summary>
/// <remarks>
///     Runs before the bundler and after the front-end files are known. It writes only into the
///     intermediate directory — nothing lands in the author's source tree, so an island project has no
///     generated files to gitignore.
/// </remarks>
public sealed class WriteExternalBuildInputsTask : Task
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

    /// <summary>
    ///     The compiled assembly, which is what actually knows each island's runtime.
    /// </summary>
    /// <remarks>
    ///     Optional, because an island can exist as a front-end file with no C# class yet — a
    ///     hand-written <c>&lt;RaskExternal&gt;</c> item, or a <c>.tsx</c> added before its component.
    ///     When it is there it WINS: see <see cref="Runtimes" />.
    /// </remarks>
    public string AssemblyPath { get; set; } = string.Empty;



    /// <summary>The generated Vite config, for the target that invokes the bundler.</summary>
    [Output]
    public string ConfigPath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var entryDirectory = Path.Combine(IntermediateDirectory, "entries");
        var islands = new List<ExternalEntry>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declared = Runtimes();

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
            // the same collision as RASK058 against the C# declarations; this catches the case where
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

            // The C# class first, the file extension only as a fallback. An extension used to name a
            // runtime; with seven of them it names a FAMILY — React, Preact and Solid all write .tsx,
            // Lit and Angular both write .ts — so the glob that discovered this file cannot know which
            // one it belongs to, and guessing is silent: a Solid island handed React's adapter builds,
            // bundles, ships, loads, and mounts nothing.
            var runtime = declared.TryGetValue(name, out var fromCSharp)
                ? fromCSharp
                : item.GetMetadata("Runtime");

            if (string.IsNullOrEmpty(runtime))
            {
                runtime = ExternalRuntime.React.Key;
            }

            // Refused rather than defaulted. An unknown runtime used to fall through to React, which
            // meant a typo in a hand-written <RaskExternal Runtime="vue3"/> generated a React entry
            // for a Vue component: the build succeeds, the bundle ships, the chunk loads, and nothing
            // mounts — with the browser reporting a failure that names none of this.
            if (ExternalRuntime.Find(runtime) is null)
            {
                Log.LogError(
                    $"Rask islands: '{source}' declares the runtime '{runtime}', which Rask has no adapter for. "
                    + $"Known runtimes: {ExternalRuntime.KeyList}.");
                return false;
            }

            islands.Add(new ExternalEntry
            {
                Name = name,
                Source = source,
                Runtime = runtime,
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
            written += ExternalBuildPlan.WriteIfDifferent(entry, ExternalBuildPlan.EntryModule(island, AdapterDirectory))
                ? 1
                : 0;
        }

        // The Angular plugin has to be told which tsconfig to compile against, and it has to be one
        // Rask writes: the app's own carries "noEmit", which makes ngtsc emit nothing and every .ts
        // island lose its default export. Written before the config that names it.
        string? angularTsConfig = null;
        if (ExternalBuildPlan.AngularTsConfig(islands, IntermediateDirectory.TrimEnd('/', '\\')) is { } ngConfig)
        {
            angularTsConfig = Path.Combine(IntermediateDirectory, "tsconfig.angular.build.json");
            written += ExternalBuildPlan.WriteIfDifferent(angularTsConfig, ngConfig) ? 1 : 0;
        }

        ConfigPath = Path.Combine(IntermediateDirectory, "vite.islands.config.mjs");

        string config;
        try
        {
            config = ExternalBuildPlan.ViteConfig(
                islands, entryDirectory, OutputDirectory, ManifestPath, PublicBase, angularTsConfig);
        }
        catch (InvalidOperationException ex)
        {
            // A combination no generated config could build correctly — two JSX runtimes sharing a
            // directory, or React beside Preact. Reported as a build error rather than written out,
            // because both alternatives are silent: one ships a bundle that mounts nothing, the other
            // fails inside npm with a message naming neither island.
            Log.LogError(ex.Message);
            return false;
        }

        written += ExternalBuildPlan.WriteIfDifferent(ConfigPath, config) ? 1 : 0;

        Log.LogMessage(
            written > 0 ? MessageImportance.High : MessageImportance.Low,
            $"Rask islands: {islands.Count} island(s), {written} build input(s) written.");

        return true;
    }

    /// <summary>
    ///     Each island's runtime as its C# class declared it, keyed by component name.
    /// </summary>
    /// <remarks>
    ///     Empty is not an error. A project can have a front-end file before it has the component, and
    ///     a hand-written <c>&lt;RaskExternal&gt;</c> item never has one; those keep the runtime the
    ///     item declared.
    /// </remarks>
    private Dictionary<string, string> Runtimes()
    {
        try
        {
            return ExternalIslandMetadata.Runtimes(AssemblyPath);
        }
        catch (Exception ex)
        {
            // Not fatal on its own: the extension fallback still produces a buildable config for the
            // single-runtime projects that are the common case. Loud, though, because in a project
            // mixing .tsx runtimes the fallback is exactly the silent mis-pairing this read exists to
            // prevent.
            Log.LogWarning(
                $"Rask islands: could not read the declared runtimes from '{AssemblyPath}' ({ex.Message}). "
                + "Falling back to the file extension, which cannot tell React, Preact and Solid apart.");

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
