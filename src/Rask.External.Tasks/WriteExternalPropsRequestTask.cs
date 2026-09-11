using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Writes the request the props extractor reads: which TypeScript to load, where the project is, and which
///     package components to describe.
/// </summary>
/// <remarks>
///     A file rather than command-line arguments: a project can hold many package islands, a module specifier can
///     carry characters a shell treats specially, and a JSON document is one quoting rule instead of one per
///     platform. Written by hand, like every other JSON this assembly writes, because an MSBuild task loaded into
///     the build host should not bring a serializer with it.
/// </remarks>
public sealed class WriteExternalPropsRequestTask : Task
{
    /// <summary>
    ///     The package islands. The item is the snapshot path; <c>IslandName</c>, <c>Runtime</c> and
    ///     <c>PackageModule</c> describe the island.
    /// </summary>
    [Required]
    public ITaskItem[] Islands { get; set; } = [];

    /// <summary>The Rask-pinned <c>lib/typescript.js</c> the extractor loads.</summary>
    [Required]
    public string TypeScriptPath { get; set; } = string.Empty;

    /// <summary>The project directory, which module resolution starts from.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>Where the extractor writes each extracted snapshot and its <c>result.json</c>.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>The request file to write.</summary>
    [Required]
    public string RequestPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"typescript\": ").Append(JsonText.String(TypeScriptPath)).Append(",\n");
        sb.Append("  \"projectDirectory\": ").Append(JsonText.String(ProjectDirectory)).Append(",\n");
        sb.Append("  \"islands\": [");

        var first = true;
        foreach (var item in Islands)
        {
            var name = item.GetMetadata("IslandName");
            var (specifier, export) = ExternalPackageSpecifier.Split(item.GetMetadata("PackageModule"));

            if (!ExternalPackageSpecifier.IsValidExport(export))
            {
                // Refused here as well as by the scan: the export is written into generated JavaScript, and an
                // export that is not an identifier is how a Module string would end an import and start code.
                Log.LogError(
                    subcategory: null, errorCode: ExternalDiagnosticCodes.InvalidDeclaration, helpKeyword: null,
                    file: item.GetMetadata("DeclaringFile"), lineNumber: LineOf(item), columnNumber: 0,
                    endLineNumber: 0, endColumnNumber: 0,
                    message: $"Rask.External: '{name}' names the export '{export}', which is not an identifier — "
                             + "write the export's exact name after the '#'.");
                continue;
            }

            sb.Append(first ? "\n" : ",\n");
            first = false;
            sb.Append("    { \"name\": ").Append(JsonText.String(name))
                .Append(", \"runtime\": ").Append(JsonText.String(item.GetMetadata("Runtime")))
                .Append(", \"module\": ").Append(JsonText.String(specifier))
                .Append(", \"export\": ").Append(JsonText.String(export))
                .Append(", \"out\": ").Append(JsonText.String(Path.Combine(OutputDirectory, name + ".props.json")))
                .Append(" }");
        }

        sb.Append("\n  ]\n}\n");

        if (Log.HasLoggedErrors)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(RequestPath)!);
        File.WriteAllText(RequestPath, sb.ToString(), new UTF8Encoding(false));
        return true;
    }

    private static int LineOf(ITaskItem item) =>
        int.TryParse(item.GetMetadata("ModuleLine"), out var line) ? line : 0;
}
