using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rask.Core.Dom.Build;

// Rask.Web's build step (src/Rask.Web/Rask.Web.targets): the web APIs from the same snapshot as the elements, written
// into obj/ before the compile. Nothing it writes is committed.
public sealed class RaskWebEmit : Task
{
    [Required] public string Snapshot { get; set; } = "";

    [Required] public string OutputDirectory { get; set; } = "";

    // Rask.Wasm's files instead: what only WebAssembly can run, as extensions of what Rask.Web declares.
    public bool Wasm { get; set; }

    public override bool Execute()
    {
        IReadOnlyList<KeyValuePair<string, string>> files;
        try
        {
            files = WebEmitter.Emit(File.ReadAllText(Snapshot), Wasm);
        }
        catch (DomEmitException e)
        {
            Log.LogError(null, "RASKDOM001", null, Snapshot, 0, 0, 0, 0, e.Message);
            return false;
        }

        Directory.CreateDirectory(OutputDirectory);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = Path.Combine(OutputDirectory, file.Key);
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

        return true;
    }
}
