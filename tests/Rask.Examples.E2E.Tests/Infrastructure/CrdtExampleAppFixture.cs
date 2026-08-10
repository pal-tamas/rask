namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     The CRDT sample. cr-sqlite's native binary is per-platform and not in this repo, so the path is
///     passed through from the environment: with it the sample runs three real replicas, without it the
///     page explains what to download. Both states are asserted — see <c>CrdtExampleTests</c>.
/// </summary>
public sealed class CrdtExampleAppFixture : ExampleAppFixture
{
    /// <summary>Whether cr-sqlite is available to this run.</summary>
    public static bool ExtensionAvailable =>
        Environment.GetEnvironmentVariable("RASK_CRSQLITE_PATH") is { Length: > 0 } path && File.Exists(path);

    protected override string ProjectRelativePath => "samples/Rask.Example.Crdt";

    protected override IReadOnlyDictionary<string, string>? ExtraEnvironment =>
        ExtensionAvailable
            ? new Dictionary<string, string>
            {
                ["RASK_CRSQLITE_PATH"] = Environment.GetEnvironmentVariable("RASK_CRSQLITE_PATH")!,
            }
            : null;
}
