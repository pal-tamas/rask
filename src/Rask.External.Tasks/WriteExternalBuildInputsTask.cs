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



    /// <summary>
    ///     Where <c>rask dev</c> serves the islands from, or empty for an ordinary build.
    /// </summary>
    /// <remarks>
    ///     Present, the manifest is written HERE with dev-server URLs instead of by the bundler with
    ///     hashed chunk paths — the bundler does not run at all under <c>rask dev</c>. Same file, same
    ///     shape, so the client runtime resolves an island one way in both modes.
    /// </remarks>
    public string DevServerUrl { get; set; } = string.Empty;

    /// <summary>The generated Vite config, for the target that invokes the bundler.</summary>
    [Output]
    public string ConfigPath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var entryDirectory = Path.Combine(IntermediateDirectory, "entries");
        var islands = CollectIslands();
        if (islands is null)
        {
            return false;
        }

        if (islands.Count == 0)
        {
            // Nothing to build. Not an error: the targets only call this when a front-end file was
            // found, but a project can lose its last island without the build becoming wrong.
            return true;
        }

        if (WriteEntries(islands, entryDirectory) is not { } entries
            || WriteConfigs(islands, entryDirectory) is not { } configs)
        {
            return false;
        }

        var written = entries + configs;
        Log.LogMessage(
            written > 0 ? MessageImportance.High : MessageImportance.Low,
            $"Rask islands: {islands.Count} island(s), {written} build input(s) written.");

        return true;
    }

    /// <summary>Every island as the bundler sees it, or null when one of them was refused (and logged).</summary>
    private List<ExternalEntry>? CollectIslands()
    {
        var islands = new List<ExternalEntry>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declared = Runtimes();

        foreach (var item in Islands)
        {
            if (EntryFor(item, seen, declared) is not { } entry)
            {
                return null;
            }

            islands.Add(entry);
        }

        return islands;
    }

    private ExternalEntry? EntryFor(ITaskItem item, Dictionary<string, string> seen, Dictionary<string, string> declared)
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
            Error(
                ExternalDiagnosticCodes.DuplicateIslandName,
                $"Rask islands: '{source}' and '{already}' would both register as '{name}'. "
                + "The island name is the key the browser resolves a module by, so it has to be "
                + "unique. Rename one of the files, and its C# class with it.");
            return null;
        }

        seen[name] = source;

        if (RuntimeFor(item, name, source, declared) is not { } runtime)
        {
            return null;
        }

        var package = item.GetMetadata("PackageModule");
        var export = item.GetMetadata("PackageExport");
        return new ExternalEntry
        {
            Name = name,
            Source = source,
            Runtime = runtime,
            Package = string.IsNullOrEmpty(package) ? null : package,
            Export = string.IsNullOrEmpty(export) ? "default" : export,
            // A Lit element named by its class mounts by the tag its snapshot recorded; the item IS the snapshot.
            Tag = !string.IsNullOrEmpty(package)
                  && string.Equals(runtime, ExternalRuntime.Lit.Key, StringComparison.Ordinal)
                  && File.Exists(source)
                ? ExternalBuildPlan.SnapshotTag(File.ReadAllText(source))
                : null,
        };
    }

    /// <summary>The island's runtime, or null when it names one Rask has no adapter for (and that was logged).</summary>
    private string? RuntimeFor(ITaskItem item, string name, string source, Dictionary<string, string> declared)
    {
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
            Error(
                ExternalDiagnosticCodes.UnknownRuntime,
                $"Rask islands: '{source}' declares the runtime '{runtime}', which Rask has no adapter for. "
                + $"Use one of: {ExternalRuntime.KeyList}.");
            return null;
        }

        return runtime;
    }

    /// <summary>Writes one entry module per island; returns how many changed, or null when one was refused.</summary>
    private int? WriteEntries(List<ExternalEntry> islands, string entryDirectory)
    {
        var written = 0;
        foreach (var island in islands)
        {
            string module;
            try
            {
                module = ExternalBuildPlan.EntryModule(island, AdapterDirectory);
            }
            catch (ExternalBuildException ex)
            {
                // A declaration no entry can be written for — a Lit element named by its class whose snapshot records no
                // tag. Reported as the build error it is, naming the fix, rather than as MSB4018 and a stack trace.
                Error(ex.Code, ex.Message);
                return null;
            }

            var entry = Path.Combine(entryDirectory, island.Name + ".entry.ts");
            written += ExternalBuildPlan.WriteIfDifferent(entry, module) ? 1 : 0;
        }

        return written;
    }

    /// <summary>Writes the Angular tsconfig, the Vite config and the dev manifest; returns how many changed, or null.</summary>
    private int? WriteConfigs(List<ExternalEntry> islands, string entryDirectory)
    {
        var written = 0;

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
                islands, entryDirectory, OutputDirectory, ManifestPath, PublicBase, angularTsConfig,
                string.IsNullOrEmpty(DevServerUrl) ? null : DevServerUrl,
                Path.Combine(IntermediateDirectory, "types", ExternalBuildPlan.RoutesModule));
        }
        catch (ExternalBuildException ex)
        {
            // A combination no generated config could build correctly — two JSX runtimes sharing a
            // directory, or React beside Preact. Reported as a build error rather than written out,
            // because both alternatives are silent: one ships a bundle that mounts nothing, the other
            // fails inside npm with a message naming neither island.
            Error(ex.Code, ex.Message);
            return null;
        }

        written += ExternalBuildPlan.WriteIfDifferent(ConfigPath, config) ? 1 : 0;

        // Under `rask dev` the bundler never runs, so nothing else would write the manifest the client
        // resolves through. Written here instead, pointing at the dev server rather than at chunks
        // that do not exist.
        if (!string.IsNullOrEmpty(DevServerUrl))
        {
            written += ExternalBuildPlan.WriteIfDifferent(
                ManifestPath,
                ExternalBuildPlan.DevManifest(islands, entryDirectory, DevServerUrl))
                ? 1
                : 0;
        }

        return written;
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
                subcategory: null, warningCode: ExternalDiagnosticCodes.UnreadableRuntimes, helpKeyword: null,
                file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                message: $"Rask islands: could not read the declared runtimes from '{AssemblyPath}' ({ex.Message}). "
                + "Falling back to the file extension, which cannot tell React, Preact and Solid apart — "
                + "rebuild the project (dotnet build --no-incremental) so the assembly is written afresh.");

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // The long overload purely to carry the CODE: an error logged without one cannot be looked up.
    private void Error(string code, string message) =>
        Log.LogError(
            subcategory: null, errorCode: code, helpKeyword: null,
            file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0, message: message);
}
