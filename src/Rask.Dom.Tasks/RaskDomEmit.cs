// Rask.Core's build-time MDN tasks, loaded by src/Rask.Core/Dom/Rask.Dom.targets. Two jobs:
//   RaskMdnRefresh  keeps mdn.snapshot.json on MDN's latest stable data (local builds, at most once a day)
//   RaskDomEmit     writes one partial per DOM interface, and the Keys/Codes constants, into obj/, from the snapshot, before the compile
// The emitted code is ordinary source to the chain generator, which is why it is written here and not by a
// Roslyn generator: generators never see each other's output.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rask.Core.Dom.Build;

public sealed class RaskDomEmit : Task
{
    [Required] public string Snapshot { get; set; } = "";

    // The hand-written partials beside the generated types: a member one declares is not generated.
    public ITaskItem[] Partials { get; set; } = Array.Empty<ITaskItem>();

    [Required] public string OutputDirectory { get; set; } = "";

    // Where the browser half (rask-dom-events.ts) goes: beside the scripts that import it.
    [Required] public string ScriptDirectory { get; set; } = "";

    [Output] public ITaskItem[] Generated { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        var partials = DomEmitter.ReadPartials(Partials.Select(p => File.ReadAllText(p.ItemSpec)));
        IReadOnlyList<KeyValuePair<string, string>> files;
        try
        {
            var snapshot = File.ReadAllText(Snapshot);
            files = DomEmitter.Emit(snapshot, partials).Concat(KeyboardValueEmitter.Emit(snapshot)).ToList();
        }
        catch (DomEmitException e)
        {
            Log.LogError(null, "RASKDOM001", null, Snapshot, 0, 0, 0, 0, e.Message);
            return false;
        }

        Directory.CreateDirectory(OutputDirectory);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(ScriptDirectory);
        foreach (var file in files)
        {
            var path = Path.Combine(file.Key.EndsWith(".ts", StringComparison.Ordinal) ? ScriptDirectory : OutputDirectory, file.Key);
            written.Add(path);
            // Only-if-changed, so an unchanged snapshot never retriggers the compile.
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), file.Value, StringComparison.Ordinal))
            {
                File.WriteAllText(path, file.Value);
            }
        }

        foreach (var stale in Directory.GetFiles(OutputDirectory, "*.g.cs").Where(f => !written.Contains(f)))
        {
            File.Delete(stale);
        }

        Generated = written.OrderBy(p => p, StringComparer.Ordinal).Select(p => (ITaskItem)new TaskItem(p)).ToArray();
        return true;
    }
}
