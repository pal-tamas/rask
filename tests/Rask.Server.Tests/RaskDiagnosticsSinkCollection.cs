namespace Rask.Server.Tests;

/// <summary>
/// Serializes every test class that swaps <c>RaskDiagnostics.Sink</c> to capture events.
/// </summary>
/// <remarks>
/// The sink is a PROCESS-WIDE static and xUnit runs test classes in parallel, so two classes capturing at
/// once means one of them installs its sink, the other replaces it, and the first sees an EMPTY capture.
/// The failure names nothing — an assertion against an empty collection — reproduces only when the machine
/// is loaded enough for the two to overlap, and passes the moment either is run alone, which is the
/// signature that gets it dismissed as a flake.
/// <para>
/// It lives in the root namespace rather than beside any one of them because the classes that need it are
/// in three different namespaces; a collection attached to one of those is a fence around one class.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RaskDiagnosticsSinkCollection
{
    /// <summary>The collection name, so a new capturing test cannot mistype its way out of the fence.</summary>
    public const string Name = "RaskDiagnosticsSink";
}
