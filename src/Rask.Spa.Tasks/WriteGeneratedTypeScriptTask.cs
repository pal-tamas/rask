using System;
using System.IO;
using Microsoft.Build.Framework;
using Rask.External.Tasks;
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

    /// <summary>
    ///     The client's TypeScript configuration. Contracts are only written into a client that has one:
    ///     without it nothing checks them, and a renamed property is found on the wire instead of at the
    ///     compiler (RASKSPA004).
    /// </summary>
    [Required]
    public string TypeScriptConfig { get; set; } = string.Empty;

    /// <summary>Whether anything was written, so the caller can say so only when it happened.</summary>
    [Output]
    public bool Changed { get; private set; }

    /// <summary>
    ///     Whether the assembly declares any remote message. A host with none gets nothing generated into
    ///     its front end, whatever language that front end is written in.
    /// </summary>
    [Output]
    public bool HasContracts { get; private set; }

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
            var constants = GeneratedTypeScript.Read(AssemblyPath, "Rask.Cqrs.Generated", "RaskGeneratedTypeScript");
            if (constants.Count == 0)
            {
                // No remote contracts in this assembly. Common and fine — a host whose front end
                // only fetches static data has nothing to describe.
                Log.LogMessage(
                    MessageImportance.Low,
                    $"Rask SPA TypeScript: '{Path.GetFileName(AssemblyPath)}' declares no remote contracts.");
                return true;
            }

            HasContracts = true;
            if (!File.Exists(TypeScriptConfig))
            {
                LogMissingTypeScriptConfig();
                return false;
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

    private void LogMissingTypeScriptConfig()
    {
        var client = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(TypeScriptConfig)));
        Log.LogError(
            subcategory: null,
            errorCode: "RASKSPA004",
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message:
            $"Rask.Spa.Hosting: this app declares remote messages, and '{client}' has no " +
            $"{Path.GetFileName(TypeScriptConfig)} to check their generated TypeScript contracts with. Scaffold " +
            "the client from its framework's TypeScript template (`npm create vite@latest -- --template " +
            "react-ts`), or point RaskSpaTypeScriptConfig at the config it does have. To serve this bundle " +
            "without the generated contracts, set RaskEmitTypeScript=false.");
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
