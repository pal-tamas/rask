using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Separates the island modules from the scoped TypeScript, for the two file lists that would
///     otherwise claim each other's <c>.ts</c> files.
/// </summary>
/// <remarks>
///     <para>
///         Both features are spelled <c>Name.ts</c> beside <c>Name.cs</c>. Island discovery used to
///         take every such pair as a Lit island, so a project with scoped TypeScript handed the bundler
///         modules that never default-exported a tag name; and the scoped glob is <c>**\*.ts</c>, so it
///         took every island's module as a component asset. Neither could be told from the other by
///         name, and the two opt-outs that existed only let a project say which ONE of the features it
///         had (#938).
///     </para>
///     <para>
///         This runs the scan in <see cref="ExternalSourceScan" /> once and answers both questions from
///         it, so the two lists are partitioned by one verdict rather than by two rules that can
///         disagree.
///     </para>
///     <para>
///         Deliberately the SOURCE scan rather than the compiled assembly, which
///         <see cref="ExternalIslandMetadata" /> can read exactly. The scoped pipeline compiles its
///         files and hands the output to csc, so its list has to be right before the compile the
///         assembly comes out of. Reading the previous build's would make a clean build and the one
///         after it disagree.
///     </para>
/// </remarks>
public sealed class FindExternalIslandsTask : Task
{
    /// <summary>The project's C# files — what the base classes are read from.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>
    ///     The <c>.ts</c> files island discovery is considering.
    /// </summary>
    /// <remarks>
    ///     Held as absolute paths, and handed back that way: <c>@(_RaskExternalFile)</c> is read by the
    ///     tasks that write import specifiers into generated JavaScript, where a relative path with no
    ///     leading <c>./</c> is a bare specifier the bundler resolves against <c>node_modules</c>.
    /// </remarks>
    public ITaskItem[] Candidates { get; set; } = [];

    /// <summary>
    ///     The scoped-TypeScript files, so the islands among them can be named for removal.
    /// </summary>
    /// <remarks>
    ///     A second list rather than one filtered set, because the two globs are not the same: the
    ///     scoped one excludes <c>Resources\**</c> and <c>Browser\**</c>, and its items carry a
    ///     <c>%(RecursiveDir)</c> that only the glob that produced them can populate. Removing by the
    ///     identity that came IN is what keeps the survivors — and that metadata — untouched.
    /// </remarks>
    public ITaskItem[] ScopedFiles { get; set; } = [];

    /// <summary>
    ///     The candidates a component actually declares as its module, carrying <c>IslandName</c> and
    ///     the runtime its base class named.
    /// </summary>
    [Output]
    public ITaskItem[] Islands { get; private set; } = [];

    /// <summary>The scoped files that are island modules, to be removed from the scoped list.</summary>
    [Output]
    public ITaskItem[] ScopedIslands { get; private set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        var sources = new List<string>(Sources.Length);
        foreach (var item in Sources)
        {
            var path = item.GetMetadata("FullPath");
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(path);
            }
        }

        var runtimes = ExternalSourceScan.IslandRuntimes(sources);

        var islands = new List<ITaskItem>();
        foreach (var item in Candidates)
        {
            if (ExternalSourceScan.RuntimeOfModule(runtimes, item.GetMetadata("FullPath")) is not { } runtime)
            {
                continue;
            }

            var claimed = new TaskItem(item);
            claimed.SetMetadata("IslandName", Path.GetFileNameWithoutExtension(item.GetMetadata("FullPath")));
            claimed.SetMetadata("Runtime", runtime);
            islands.Add(claimed);
        }

        var scoped = new List<ITaskItem>();
        foreach (var item in ScopedFiles)
        {
            if (ExternalSourceScan.RuntimeOfModule(runtimes, item.GetMetadata("FullPath")) is not null)
            {
                // The item ITSELF, so the Remove in the targets matches on the identity the scoped
                // glob produced rather than on a normalized copy of it.
                scoped.Add(item);
            }
        }

        Islands = islands.ToArray();
        ScopedIslands = scoped.ToArray();

        Log.LogMessage(
            MessageImportance.Low,
            $"Rask islands: {islands.Count} of {Candidates.Length} '.ts' candidate(s) are island modules; "
            + $"{scoped.Count} removed from the scoped-TypeScript list.");

        return true;
    }
}
