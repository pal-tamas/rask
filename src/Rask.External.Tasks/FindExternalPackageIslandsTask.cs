using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Finds the package islands — classes whose constant <c>Module</c> names an npm package — before the compile,
///     so their props can be extracted into the snapshot the compile reads.
/// </summary>
/// <remarks>
///     <para>
///         Each island comes back as an item whose identity is its snapshot path, beside the file that overrides
///         <c>Module</c>. Carrying the declaring file and line lets every later diagnostic point at the one line
///         the author wrote, rather than at a JSON file they did not.
///     </para>
///     <para>
///         The items join <c>@(_RaskExternalFile)</c>, so every island gate that already exists — the node probe,
///         the install, the entry modules, the bundle, the static web assets — includes them without being taught
///         a second list.
///     </para>
/// </remarks>
public sealed class FindExternalPackageIslandsTask : Task
{
    private static readonly Dictionary<string, string[]> SiblingExtensions = new(StringComparer.Ordinal)
    {
        ["react"] = [".tsx", ".jsx"],
        ["preact"] = [".tsx", ".jsx"],
        ["solid"] = [".tsx", ".jsx"],
        ["vue"] = [".vue"],
        ["svelte"] = [".svelte"],
        ["lit"] = [".ts"],
        ["angular"] = [".ts"],
    };

    /// <summary>The project's C# files.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>The project directory, which relative <see cref="Sources" /> are resolved against.</summary>
    /// <remarks>
    ///     Passed in rather than read from <c>%(FullPath)</c>. Inside a task that metadata resolves a relative item
    ///     against the process's current directory, which the OS reports with symlinks resolved — macOS spells
    ///     <c>/var/folders</c> as <c>/private/var/folders</c> — while the evaluation-time snapshot glob keeps the
    ///     project's own spelling. The two then name one file and compare unequal, and the snapshot reached the
    ///     compile twice. Empty falls back to <c>%(FullPath)</c>.
    /// </remarks>
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>
    ///     The package islands: the item is the snapshot path, with <c>IslandName</c>, <c>Runtime</c>,
    ///     <c>PackageModule</c>, <c>DeclaringFile</c>, <c>ModuleLine</c> and <c>Extractable</c> — whether the
    ///     extractor reads this runtime's packages yet.
    /// </summary>
    [Output]
    public ITaskItem[] PackageIslands { get; private set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        var sources = new List<string>(Sources.Length);
        foreach (var item in Sources)
        {
            var path = PathOf(item);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(path);
            }
        }

        var runtimes = ExternalSourceScan.IslandRuntimes(sources);
        var items = new List<ITaskItem>();

        foreach (var island in ExternalPackageScan.PackageIslands(sources, runtimes))
        {
            var (_, export) = ExternalPackageSpecifier.Split(island.Module);
            if (!ExternalPackageSpecifier.IsValidExport(export))
            {
                Error(island,
                    $"Rask.External: '{island.Name}' names the export '{export}', which is not an identifier — write "
                    + "the export's exact name after the '#'.");
                continue;
            }

            if (SiblingOf(island) is { } sibling)
            {
                // Both would register under one name: the file as the island's module, the package as its Module.
                // Whichever the bundler met last would win, silently.
                Error(island,
                    $"Rask.External: '{island.Name}' names the package '{island.Module}' as its Module, but "
                    + $"{Path.GetFileName(sibling)} sits beside it as well — remove the Module override to use the "
                    + "file, or delete the file to use the package.");
                continue;
            }

            var item = new TaskItem(island.SnapshotPath);
            item.SetMetadata("IslandName", island.Name);
            item.SetMetadata("Runtime", island.Runtime);
            item.SetMetadata("PackageModule", island.Module);
            item.SetMetadata("DeclaringFile", island.DeclaringFile);
            item.SetMetadata("ModuleLine", island.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.SetMetadata("Extractable", ExternalRuntime.Find(island.Runtime)?.PropsExtracted == true ? "true" : "false");
            items.Add(item);
        }

        PackageIslands = items.ToArray();
        Log.LogMessage(MessageImportance.Low, $"Rask islands: {items.Count} package island(s) found.");
        return !Log.HasLoggedErrors;
    }

    /// <summary>An absolute path for a source item, spelled from <see cref="ProjectDirectory" /> — see its remarks.</summary>
    private string PathOf(ITaskItem item)
    {
        var spec = item.ItemSpec;
        if (string.IsNullOrEmpty(ProjectDirectory) || string.IsNullOrEmpty(spec))
        {
            return item.GetMetadata("FullPath");
        }

        // GetFullPath over an already-rooted path only normalizes separators and '..'; it never consults the
        // current directory, so the project's own spelling survives.
        return Path.GetFullPath(Path.IsPathRooted(spec) ? spec : Path.Combine(ProjectDirectory, spec));
    }

    private static string? SiblingOf(ScannedPackageIsland island)
    {
        if (!SiblingExtensions.TryGetValue(island.Runtime, out var extensions))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(island.DeclaringFile) ?? string.Empty;
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory, island.Name + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Error(ScannedPackageIsland island, string message) =>
        Log.LogError(
            subcategory: null, errorCode: ExternalDiagnosticCodes.InvalidDeclaration, helpKeyword: null,
            file: island.DeclaringFile, lineNumber: island.Line, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
            message: message);
}
