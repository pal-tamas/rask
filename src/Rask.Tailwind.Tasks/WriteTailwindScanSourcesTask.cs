using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     Mirrors a project's C# sources into a folder with their comments taken out, for Tailwind to scan
///     in place of the project (<c>RaskTailwindSourceRoot</c>).
/// </summary>
/// <remarks>
///     A copy is rewritten only when its text changed, so its timestamp moves exactly when the scan has
///     something new to read; a copy whose source is gone is deleted, or a class only it still wrote would
///     stay in the sheet.
/// </remarks>
public sealed class WriteTailwindScanSourcesTask : Task
{
    /// <summary>The C# files to mirror.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>The folder the sources' relative paths are taken from: the project.</summary>
    [Required]
    public string SourceRoot { get; set; } = string.Empty;

    /// <summary>The folder the copies are written to, at the same relative paths.</summary>
    [Required]
    public string Output { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var root = Path.GetFullPath(SourceRoot).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        var output = Path.GetFullPath(Output);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Sources)
        {
            var path = Path.GetFullPath(source.ItemSpec);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Log.LogError("Rask.Tailwind: {0} is not under {1}, so it has no place in the scan folder.", path, root);
                return false;
            }

            var copy = Path.Combine(output, path.Substring(root.Length));
            WriteIfChanged(copy, CSharpComments.Strip(File.ReadAllText(path)));
            written.Add(copy);
        }

        if (Directory.Exists(output))
        {
            foreach (var stale in Directory.GetFiles(output, "*", SearchOption.AllDirectories).Where(file => !written.Contains(file)))
            {
                File.Delete(stale);
            }
        }

        return true;
    }

    private static void WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
