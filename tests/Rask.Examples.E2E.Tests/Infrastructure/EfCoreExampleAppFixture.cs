namespace Rask.Examples.E2E.Tests.Infrastructure;

public sealed class EfCoreExampleAppFixture : ExampleAppFixture
{
    // A unique SQLite file per run so the CRUD test starts from the known seed and never sees
    // state left by a previous run. The sample reads RASK_DB_PATH from configuration.
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-efcore-e2e-{Guid.NewGuid():N}.db");

    // A unique pickup directory per run: with no SMTP configured, the sample's MailProcessor writes each
    // sent email here as an .eml file, so the mail test can assert delivery actually happened.
    public string MailPickupDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"rask-efcore-e2e-mail-{Guid.NewGuid():N}");

    protected override string ProjectRelativePath => "samples/Rask.Example.EfCore";

    protected override int Port => 5110;

    protected override IReadOnlyDictionary<string, string>? ExtraEnvironment =>
        new Dictionary<string, string>
        {
            ["RASK_DB_PATH"] = _dbPath,
            ["RASK_MAIL_PICKUP"] = MailPickupDirectory,
        };
}
