namespace Rask.External.Tasks;

/// <summary>The build-task diagnostic codes for package islands.</summary>
/// <remarks>
///     <para>
///         Prefixed codes rather than RASK0xx, as every island build diagnostic is: a RASK id belongs to an analyzer
///         descriptor, and these are logged by MSBuild tasks that have none. RASKISLAND001–003 live in
///         <c>Rask.External.targets</c> and 004 in <see cref="WriteExternalPropTypesTask" />.
///     </para>
///     <para>Grep <c>src/</c> for a code before claiming the next one.</para>
/// </remarks>
internal static class ExternalDiagnosticCodes
{
    /// <summary>A package island declaration the build cannot act on.</summary>
    public const string InvalidDeclaration = "RASKISLAND005";

    /// <summary>No snapshot, and its props cannot be extracted on this build.</summary>
    public const string MissingSnapshot = "RASKISLAND006";

    /// <summary>The extractor could not describe the package component.</summary>
    public const string ExtractionFailed = "RASKISLAND007";

    /// <summary>The committed snapshot is stale and the build is locked.</summary>
    public const string SnapshotDrift = "RASKISLAND008";

    /// <summary>An island the assembly declares with a package module that the source scan missed.</summary>
    public const string UnscannedPackageIsland = "RASKISLAND009";

    /// <summary>The committed snapshot was taken from a different package version than the lockfile pins.</summary>
    public const string SnapshotVersionMismatch = "RASKISLAND010";
}
