using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Rask.Spa.Tasks;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Writes each external component's generated prop types into <c>obj/</c>, and the tsconfig
///     fragment that gives them an import path.
/// </summary>
/// <remarks>
///     <para>
///         This is the half that makes the contract two-way. Without it the generator's interfaces
///         exist only as constants inside the assembly, and the <c>.tsx</c> goes on typing its props by
///         hand — so renaming a C# property breaks the front end silently, at runtime, in the browser.
///     </para>
///     <para>
///         Into <c>obj/</c> rather than beside the source, because these are build output: a generated
///         file in the source tree is one someone edits by hand and loses on the next build, and one
///         that shows up in every review diff.
///     </para>
///     <para>
///         Runs between the C# compile and the bundler: the constants only exist once Roslyn has
///         produced the assembly, and the files are inputs to whatever compiles the front end.
///     </para>
/// </remarks>
public sealed class WriteExternalPropTypesTask : Task
{
    private const string GeneratedNamespace = "Rask.External.Generated";
    private const string GeneratedTypeName = "RaskExternalGeneratedTypeScript";

    /// <summary>The just-compiled assembly to read the constants out of.</summary>
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>Where the <c>.d.ts</c> files go — under <c>obj/</c>, never the source tree.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Where the tsconfig fragment goes, for the app's tsconfig to <c>extends</c>.</summary>
    [Required]
    public string TsConfigPath { get; set; } = string.Empty;

    /// <summary>The project directory, which the emitted paths are written relative to.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!File.Exists(AssemblyPath))
        {
            // A build that produced no assembly has already failed for its own reasons; a second
            // error here would only bury the first.
            Log.LogMessage(MessageImportance.Low, $"Rask.External: no assembly at '{AssemblyPath}' — skipping.");
            return true;
        }

        try
        {
            var constants = GeneratedTypeScript.Read(AssemblyPath, GeneratedNamespace, GeneratedTypeName);
            if (constants.Count == 0)
            {
                Log.LogMessage(
                    MessageImportance.Low,
                    $"Rask.External: '{Path.GetFileName(AssemblyPath)}' declares no external components.");
                return true;
            }

            Directory.CreateDirectory(OutputDirectory);

            var written = 0;
            foreach (var pair in constants)
            {
                var path = Path.Combine(OutputDirectory, pair.Key + ".props.d.ts");
                if (GeneratedTypeScript.WriteIfDifferent(path, pair.Value))
                {
                    written++;
                }
            }

            // Stale types are worse than missing ones: a deleted component would leave a .d.ts that
            // still type-checks, so the front end would keep compiling against something the server
            // no longer renders.
            Prune(constants.Keys);

            if (WriteTsConfig())
            {
                written++;
            }

            if (written > 0)
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"Rask.External: wrote prop types for {constants.Count} component(s) to '{OutputDirectory}'.");
            }

            return true;
        }
        catch (Exception ex)
        {
            // Failing the build is right: the alternative is a front end compiling against the last
            // build's props, which type-checks and then arrives wrong in the browser.
            Log.LogError($"Rask.External: could not read the generated prop types from '{AssemblyPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Deletes the <c>.d.ts</c> of a component that no longer exists.</summary>
    private void Prune(IEnumerable<string> current)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in current)
        {
            keep.Add(name + ".props.d.ts");
        }

        foreach (var file in Directory.GetFiles(OutputDirectory, "*.props.d.ts"))
        {
            if (keep.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            File.Delete(file);
            Log.LogMessage(MessageImportance.Low, $"Rask.External: removed stale '{Path.GetFileName(file)}'.");
        }
    }

    /// <summary>
    ///     Writes the tsconfig fragment the app's own tsconfig extends.
    /// </summary>
    /// <remarks>
    ///     A fragment to `extends` rather than an edit to the app's tsconfig.json. Rewriting someone
    ///     else's config would have to preserve their comments, their key order and their formatting,
    ///     and would still surprise anyone who opened it — for a path mapping that is one line to add
    ///     once and obvious to remove.
    /// </remarks>
    private bool WriteTsConfig()
    {
        // Relative to the fragment's own directory, and forward-slashed: a Windows path inside JSON is
        // a string full of escape sequences, and tsconfig resolves relative to the file it is in.
        var relative = Relative(Path.GetDirectoryName(TsConfigPath) ?? ProjectDirectory, OutputDirectory);

        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  // <auto-generated/> Rask writes this; your tsconfig.json extends it.");
        json.AppendLine("  \"compilerOptions\": {");
        json.AppendLine("    \"baseUrl\": \".\",");
        json.AppendLine("    \"paths\": {");
        json.Append("      \"@rask/*\": [\"").Append(relative).AppendLine("/*\"]");
        json.AppendLine("    }");
        json.AppendLine("  }");
        json.AppendLine("}");

        return GeneratedTypeScript.WriteIfDifferent(TsConfigPath, json.ToString());
    }

    private static string Relative(string from, string to)
    {
        var fromUri = new Uri(AppendSeparator(Path.GetFullPath(from)));
        var toUri = new Uri(AppendSeparator(Path.GetFullPath(to)));
        var relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString()).TrimEnd('/');
        return relative.Length == 0 ? "." : relative;
    }

    private static string AppendSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}
