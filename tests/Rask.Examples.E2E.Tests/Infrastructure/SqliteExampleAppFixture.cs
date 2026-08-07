namespace Rask.Examples.E2E.Tests.Infrastructure;

public sealed class SqliteExampleAppFixture : ExampleAppFixture
{
    // A unique SQLite file per run so the concurrent-writes count starts from an empty table and never
    // sees rows left by a previous run. The sample reads RASK_DB_PATH from configuration.
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-sqlite-e2e-{Guid.NewGuid():N}.db");

    protected override string ProjectRelativePath => "samples/Rask.Example.Sqlite";
    protected override IReadOnlyDictionary<string, string>? ExtraEnvironment =>
        new Dictionary<string, string> { ["RASK_DB_PATH"] = _dbPath };
}
