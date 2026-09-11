using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Brings each committed props snapshot up to date with what the extractor just read from the package — or,
///     on a locked build, refuses when one is out of date.
/// </summary>
/// <remarks>
///     <para>
///         The snapshot is committed like a lockfile, and it is refreshed the way a lockfile is: a build that can
///         read the package rewrites a snapshot whose contents changed and says so, so the change shows up in
///         review. A prop the package removed then fails at its call sites as a compile error, which is the loud
///         half of the contract.
///     </para>
///     <para>
///         A locked build (<c>RaskExternalPropsLocked</c>, on by default under <c>ContinuousIntegrationBuild</c>)
///         never writes. A build that rewrites its own inputs cannot go red on a stale snapshot, which is right on
///         a laptop and wrong for the one run that is supposed to prove the committed files are true.
///     </para>
///     <para>
///         Only a changed file is written, so an unchanged snapshot keeps its timestamp and nothing downstream
///         rebuilds for it.
///     </para>
/// </remarks>
public sealed class SyncExternalPropsSnapshotsTask : Task
{
    private static readonly Regex ResultEntry = new(
        "\\{\\s*\"name\"\\s*:\\s*\"(?<name>(?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*\"ok\"\\s*:\\s*(?<ok>true|false)"
        + "(?:\\s*,\\s*\"code\"\\s*:\\s*\"(?<code>(?:[^\"\\\\]|\\\\.)*)\")?"
        + "(?:\\s*,\\s*\"message\"\\s*:\\s*\"(?<message>(?:[^\"\\\\]|\\\\.)*)\")?\\s*\\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex Version = new(
        "\"package\"\\s*:\\s*\\{[^}]*\"version\"\\s*:\\s*(?:\"(?<version>[^\"]*)\"|null)",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     The package islands. The item is the committed snapshot path; <c>IslandName</c>, <c>DeclaringFile</c>
    ///     and <c>ModuleLine</c> locate the island for diagnostics.
    /// </summary>
    [Required]
    public ITaskItem[] Islands { get; set; } = [];

    /// <summary>Where the extractor wrote <c>{Name}.props.json</c> and <c>result.json</c>.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Whether to refuse an out-of-date snapshot instead of rewriting it.</summary>
    public bool Locked { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        var resultPath = Path.Combine(OutputDirectory, "result.json");
        if (!File.Exists(resultPath))
        {
            Log.LogError(
                subcategory: null, errorCode: ExternalDiagnosticCodes.ExtractionFailed, helpKeyword: null,
                file: null, lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                message: "Rask.External: the props extractor wrote no result — its output above says why.");
            return false;
        }

        var results = ReadResults(File.ReadAllText(resultPath));

        foreach (var island in Islands)
        {
            var name = island.GetMetadata("IslandName");
            var file = island.GetMetadata("DeclaringFile");
            var line = int.TryParse(island.GetMetadata("ModuleLine"), out var parsed) ? parsed : 0;

            if (!results.TryGetValue(name, out var result))
            {
                Error(ExternalDiagnosticCodes.ExtractionFailed, file, line,
                    $"Rask.External: the props extractor did not report on '{name}' — rebuild; if it persists, please report it.");
                continue;
            }

            if (!result.Ok)
            {
                Error(ExternalDiagnosticCodes.ExtractionFailed, file, line,
                    $"Rask.External: the props of '{name}' could not be read from its package ({result.Code}): {result.Message}");
                continue;
            }

            var extractedPath = Path.Combine(OutputDirectory, name + ".props.json");
            var extracted = File.ReadAllText(extractedPath);
            var snapshotPath = island.ItemSpec;
            var committed = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : null;

            if (string.Equals(Normalize(committed), extracted, StringComparison.Ordinal))
            {
                continue;
            }

            var change = Describe(committed, extracted);
            if (Locked)
            {
                Error(ExternalDiagnosticCodes.SnapshotDrift, file, line,
                    $"Rask.External: {Path.GetFileName(snapshotPath)} is out of date ({change}) and this build is locked "
                    + "(RaskExternalPropsLocked) — build once without it and commit the refreshed file.");
                continue;
            }

            File.WriteAllText(snapshotPath, extracted, new UTF8Encoding(false));
            Log.LogMessage(
                MessageImportance.High,
                $"Rask.External: {(committed is null ? "wrote" : "refreshed")} {Path.GetFileName(snapshotPath)} ({change}) — commit it.");
        }

        return !Log.HasLoggedErrors;
    }

    /// <summary>The extractor's per-island verdicts, keyed by island name.</summary>
    internal static Dictionary<string, (bool Ok, string Code, string Message)> ReadResults(string json)
    {
        var results = new Dictionary<string, (bool, string, string)>(StringComparer.Ordinal);
        foreach (Match match in ResultEntry.Matches(json))
        {
            results[JsonText.Unescape(match.Groups["name"].Value)] = (
                match.Groups["ok"].Value == "true",
                JsonText.Unescape(match.Groups["code"].Value),
                JsonText.Unescape(match.Groups["message"].Value));
        }

        return results;
    }

    /// <summary>What changed, in the words a reviewer wants: a first snapshot, or a version move.</summary>
    internal static string Describe(string? committed, string extracted)
    {
        var after = VersionOf(extracted);
        if (committed is null)
        {
            return after is null ? "new" : $"new, from {after}";
        }

        var before = VersionOf(committed);
        return before is not null && after is not null && !string.Equals(before, after, StringComparison.Ordinal)
            ? $"{before} → {after}"
            : "the package's typings changed";
    }

    internal static string? VersionOf(string snapshot)
    {
        var match = Version.Match(snapshot);
        return match.Success && match.Groups["version"].Success ? match.Groups["version"].Value : null;
    }

    // A snapshot checked out on Windows can have CRLF line endings; the extractor always writes LF. That difference
    // is git's, not the package's, and must not read as drift.
    private static string? Normalize(string? text) => text?.Replace("\r\n", "\n");

    private void Error(string code, string file, int line, string message) =>
        Log.LogError(
            subcategory: null, errorCode: code, helpKeyword: null,
            file: string.IsNullOrEmpty(file) ? null : file, lineNumber: line, columnNumber: 0,
            endLineNumber: 0, endColumnNumber: 0, message: message);
}
