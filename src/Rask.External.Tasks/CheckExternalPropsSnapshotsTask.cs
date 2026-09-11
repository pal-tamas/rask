using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     On a build that cannot extract props, checks that every package island still has a committed snapshot to
///     generate from — and that it was taken from the version the lockfile pins.
/// </summary>
/// <remarks>
///     <para>
///         A committed snapshot is exactly what lets a build with no Node, or with extraction switched off, still
///         produce every chain step. So a missing one is an error here (RASKISLAND006): nothing else will create
///         it, and the island would otherwise compile with none of its props and no reason given.
///     </para>
///     <para>
///         The version check reads <c>package-lock.json</c> by hand and needs no Node, so it runs exactly where
///         extraction cannot. A mismatch is a warning (RASKISLAND010): the snapshot may still be right, but the
///         package it describes is not the one that will be bundled.
///     </para>
/// </remarks>
public sealed class CheckExternalPropsSnapshotsTask : Task
{
    /// <summary>
    ///     The package islands. The item is the snapshot path; <c>IslandName</c>, <c>PackageModule</c>,
    ///     <c>DeclaringFile</c> and <c>ModuleLine</c> describe the island.
    /// </summary>
    [Required]
    public ITaskItem[] Islands { get; set; } = [];

    /// <summary>Why this build cannot extract props, for the message.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The project's <c>package-lock.json</c>, when it has one.</summary>
    public string LockFile { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        var lockText = !string.IsNullOrEmpty(LockFile) && File.Exists(LockFile) ? File.ReadAllText(LockFile) : null;

        foreach (var island in Islands)
        {
            var name = island.GetMetadata("IslandName");
            var file = island.GetMetadata("DeclaringFile");
            var line = int.TryParse(island.GetMetadata("ModuleLine"), out var parsed) ? parsed : 0;

            if (!File.Exists(island.ItemSpec))
            {
                Log.LogError(
                    subcategory: null, errorCode: ExternalDiagnosticCodes.MissingSnapshot, helpKeyword: null,
                    file: NullIfEmpty(file), lineNumber: line, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                    message: $"Rask.External: '{name}' has no {name}.props.json, and its props cannot be extracted on "
                             + $"this build ({Reason}) — build once with the package installed and commit the file.");
                continue;
            }

            if (lockText is null)
            {
                continue;
            }

            var package = ExternalPackageSpecifier.PackageName(
                ExternalPackageSpecifier.Split(island.GetMetadata("PackageModule")).Specifier);
            var pinned = LockedVersion(lockText, package);
            var taken = SyncExternalPropsSnapshotsTask.VersionOf(File.ReadAllText(island.ItemSpec));

            if (pinned is not null && taken is not null && !string.Equals(pinned, taken, StringComparison.Ordinal))
            {
                Log.LogWarning(
                    subcategory: null, warningCode: ExternalDiagnosticCodes.SnapshotVersionMismatch, helpKeyword: null,
                    file: NullIfEmpty(file), lineNumber: line, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                    message: $"Rask.External: {name}.props.json was taken from {package} {taken}, but package-lock.json "
                             + $"pins {pinned} — build where props can be extracted to refresh it.");
            }
        }

        return !Log.HasLoggedErrors;
    }

    /// <summary>
    ///     The version <c>package-lock.json</c> (lockfile v2 or v3) pins for <paramref name="package" />'s top-level
    ///     install, or null when it pins none.
    /// </summary>
    internal static string? LockedVersion(string lockText, string package)
    {
        var key = Regex.Escape("\"node_modules/" + package + "\"");
        var match = Regex.Match(
            lockText,
            key + "\\s*:\\s*\\{[^{}]*?\"version\"\\s*:\\s*\"(?<version>[^\"]+)\"",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
