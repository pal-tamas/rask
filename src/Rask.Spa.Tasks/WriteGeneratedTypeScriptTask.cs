using System;
using System.IO;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Spa.Tasks;

/// <summary>
///     Writes the generated CQRS TypeScript into the front end's source tree, so the bundler compiles
///     the same contracts the server does.
/// </summary>
/// <remarks>
///     Runs between the C# compile and <c>npm run build</c>: the constants it reads only exist once
///     Roslyn has produced the assembly, and the files it writes are inputs to the bundler.
/// </remarks>
public sealed class WriteGeneratedTypeScriptTask : Task
{
    /// <summary>The just-compiled assembly to read the constants out of.</summary>
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>The directory the <c>.ts</c> files are written into, inside the client's sources.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Whether anything was written, so the caller can say so only when it happened.</summary>
    [Output]
    public bool Changed { get; private set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!File.Exists(AssemblyPath))
        {
            // A build that produced no assembly has already failed for its own reasons; adding a
            // second error here would only bury the first.
            Log.LogMessage(MessageImportance.Low, $"Rask SPA TypeScript: no assembly at '{AssemblyPath}' — skipping.");
            return true;
        }

        try
        {
            var constants = GeneratedTypeScript.Read(AssemblyPath);
            if (constants.Count == 0)
            {
                // No remote contracts in this assembly. Common and fine — a host whose front end
                // only fetches static data has nothing to describe.
                Log.LogMessage(
                    MessageImportance.Low,
                    $"Rask SPA TypeScript: '{Path.GetFileName(AssemblyPath)}' declares no remote contracts.");
                return true;
            }

            var written = 0;
            written += Write(constants, "Contracts", "contracts.ts") ? 1 : 0;
            written += Write(constants, "Messages", "messages.ts") ? 1 : 0;
            Changed = written > 0;

            if (Changed)
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"Rask SPA TypeScript: wrote {written} file(s) to '{OutputDirectory}'.");
            }

            return true;
        }
        catch (Exception ex)
        {
            // Failing the build here is right: the alternative is a front end compiling against last
            // build's contracts, which type-checks and then breaks on the wire.
            Log.LogError(
                $"Rask SPA TypeScript: could not read the generated contracts from " +
                $"'{AssemblyPath}' — {ex.Message}");
            return false;
        }
    }

    private bool Write(System.Collections.Generic.IReadOnlyDictionary<string, string> constants, string key, string file)
    {
        if (!constants.TryGetValue(key, out var content))
        {
            return false;
        }

        return GeneratedTypeScript.WriteIfDifferent(Path.Combine(OutputDirectory, file), content);
    }
}
