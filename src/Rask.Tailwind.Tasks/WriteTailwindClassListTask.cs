using System;
using System.IO;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     Writes the class names a compiled stylesheet defines, one per line, for another project's Tailwind
///     build to scan (<c>RaskTailwindClassList</c>).
/// </summary>
/// <remarks>
///     Nothing but the names: Tailwind reads every word of a scanned file as a candidate, so a header
///     saying what the file is would add its own words to the sheet.
/// </remarks>
public sealed class WriteTailwindClassListTask : Task
{
    /// <summary>The compiled stylesheet to read.</summary>
    [Required]
    public string Stylesheet { get; set; } = string.Empty;

    /// <summary>Where the list is written.</summary>
    [Required]
    public string Output { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var names = CssClassNames.In(File.ReadAllText(Stylesheet));
        if (names.Count == 0)
        {
            Log.LogError(
                "Rask.Tailwind: {0} defines no class at all, so the class list {1} would be empty and every "
                + "project compiling from it would render unstyled.", Stylesheet, Output);
            return false;
        }

        var text = string.Join("\n", names) + "\n";
        if (!File.Exists(Output) || !string.Equals(File.ReadAllText(Output), text, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output))!);
            File.WriteAllText(Output, text);
        }

        return true;
    }
}
