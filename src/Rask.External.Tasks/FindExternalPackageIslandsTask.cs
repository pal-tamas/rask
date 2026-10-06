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
    ///     <c>PackageModule</c>, <c>PackageExport</c> (<c>default</c> when the class names none), <c>DeclaringFile</c>
    ///     and <c>ModuleLine</c>.
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
            if (Refusal(island) is { } why)
            {
                Error(island, why);
                continue;
            }

            items.Add(ItemFor(island));
        }

        PackageIslands = items.ToArray();
        Log.LogMessage(MessageImportance.Low, $"Rask islands: {items.Count} package island(s) found.");
        return !Log.HasLoggedErrors;
    }

    /// <summary>Why a scanned island cannot be built as a package island, or null when it can.</summary>
    private static string? Refusal(ScannedPackageIsland island)
    {
        if (!island.IsPackage && island.FromDeclaration)
        {
            return
                $"Rask islands: '{island.Name}' comes from a package declaration whose Module, '{island.Module}', "
                + "names no npm package — return the package its Exports come from, e.g. "
                + "protected override string Module => \"@mui/material\";";
        }

        if (!island.IsPackage)
        {
            // Silently ignored, it would read as the island's component while the build mounts the file's default.
            return
                $"Rask islands: '{island.Name}' overrides Export, but its Module names no package — Export picks a "
                + "component out of an npm package, so return the package from Module as well "
                + "(protected override string Module => \"react-colorful\";) or remove the Export override.";
        }

        var hash = island.Module.IndexOf('#');
        if (hash > 0)
        {
            // The old spelling. Refused rather than read, so there is one way to name an export.
            return
                $"Rask islands: '{island.Name}' writes its export after a '#' in Module — name it in Export "
                + $"instead: protected override string Module => \"{island.Module.Substring(0, hash)}\"; "
                + $"protected override string Export => \"{island.Module.Substring(hash + 1)}\";";
        }

        var export = island.ExportOrDefault;
        if (!ExternalPackageSpecifier.IsValidExport(export, island.Runtime))
        {
            return
                $"Rask islands: '{island.Name}' names the export '{export}', which is not an identifier — return "
                + "the export's exact name from Export (a Lit island may name the tag its module registers).";
        }

        if (SiblingOf(island) is { } sibling)
        {
            // Both would register under one name: the file as the island's module, the package as its Module.
            // Whichever the bundler met last would win, silently.
            return
                $"Rask islands: '{island.Name}' names the package '{island.Module}' as its Module, but "
                + $"{Path.GetFileName(sibling)} sits beside it as well — remove the Module override to use the "
                + "file, or delete the file to use the package.";
        }

        return null;
    }

    private static TaskItem ItemFor(ScannedPackageIsland island)
    {
        var item = new TaskItem(island.SnapshotPath);
        item.SetMetadata("IslandName", island.Name);
        item.SetMetadata("Runtime", island.Runtime);
        item.SetMetadata("PackageModule", island.Module);
        item.SetMetadata("PackageExport", island.ExportOrDefault);
        item.SetMetadata("DeclaringFile", island.DeclaringFile);
        item.SetMetadata("ModuleLine", island.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return item;
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
