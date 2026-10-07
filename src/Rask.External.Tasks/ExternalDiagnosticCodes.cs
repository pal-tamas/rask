namespace Rask.External.Tasks;

/// <summary>The diagnostic codes the island build tasks log.</summary>
/// <remarks>
///     <para>
///         Prefixed codes rather than RASK0xx, as every island build diagnostic is: a RASK id belongs to an analyzer
///         descriptor, and these are logged by MSBuild tasks that have none. RASKISLAND001–003 live in
///         <c>Rask.External.targets</c>; every other one is allocated here.
///     </para>
///     <para>
///         A code is what makes a task diagnostic reachable at all: one logged without it cannot be demoted by
///         <c>MSBuildWarningsAsMessages</c>, promoted, or looked up. Each is documented in
///         <c>docs/diagnostics.md</c>.
///     </para>
///     <para>Grep the repository for a code before claiming the next one.</para>
/// </remarks>
internal static class ExternalDiagnosticCodes
{
    /// <summary>A declared island's front-end file is not among the files the build will bundle.</summary>
    public const string UnbuiltIsland = "RASKISLAND004";

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

    /// <summary>Two front-end files would register under one island name.</summary>
    public const string DuplicateIslandName = "RASKISLAND011";

    /// <summary>An island names a runtime Rask has no adapter for.</summary>
    public const string UnknownRuntime = "RASKISLAND012";

    /// <summary><c>RaskExternalDevServerUrl</c> is not an http(s) origin.</summary>
    public const string InvalidDevServerUrl = "RASKISLAND013";

    /// <summary>React and Preact islands in one project: their Vite plugins cannot be installed together.</summary>
    public const string ReactBesidePreact = "RASKISLAND014";

    /// <summary>Two runtimes that compile the same extension share a directory tree.</summary>
    public const string OverlappingRuntimeTrees = "RASKISLAND015";

    /// <summary>A package island needs a plugin that a rival runtime's islands also claim, and has no folder to scope it to.</summary>
    public const string UnscopablePackageIsland = "RASKISLAND016";

    /// <summary>The generated prop types could not be read out of the compiled assembly.</summary>
    public const string UnreadablePropTypes = "RASKISLAND017";

    /// <summary>The declared runtimes could not be read out of the compiled assembly, so the file extension decides.</summary>
    public const string UnreadableRuntimes = "RASKISLAND018";
}
