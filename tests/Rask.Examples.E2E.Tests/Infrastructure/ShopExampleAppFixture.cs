namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
/// Boots <c>samples/Rask.Example.Shop</c> — the CLI-generated app that wires every OPF battery.
/// </summary>
/// <remarks>
/// Everything is driven through ordinary configuration keys rather than sample-only environment variables:
/// the app reads <c>ConnectionStrings:App</c>, <c>Mail:PickupDirectory</c> and <c>Sqlite:SnapshotDirectory</c>
/// exactly as a deployed instance would, and ASP.NET's <c>__</c> convention maps them from the environment.
/// A fresh path per run means each test starts from the known seed instead of a previous run's state.
/// </remarks>
public sealed class ShopExampleAppFixture : ExampleAppFixture
{
    private readonly string _id = Guid.NewGuid().ToString("N");

    /// <summary>Where the sample's MailProcessor writes each sent message, with no SMTP configured.</summary>
    public string MailPickupDirectory => Path.Combine(Path.GetTempPath(), $"rask-shop-e2e-mail-{_id}");

    /// <summary>Where Rask.SQLite.Snapshots writes its scheduled backups.</summary>
    public string SnapshotDirectory => Path.Combine(Path.GetTempPath(), $"rask-shop-e2e-snap-{_id}");

    private string DbPath => Path.Combine(Path.GetTempPath(), $"rask-shop-e2e-{_id}.db");

    protected override string ProjectRelativePath => "samples/Rask.Example.Shop";
    // Run the published app, like the Server showcase does. A plain `dotnet run` over in-repo project
    // references never materialises the `_content/Rask.Bootstrap/*` static assets the shell links, so the
    // page loads with failing stylesheet requests — which is not how anyone deploys it, and not what these
    // tests should be asserting against.
    protected override bool RunPublished => true;

    protected override IReadOnlyDictionary<string, string>? ExtraEnvironment =>
        new Dictionary<string, string>
        {
            ["ConnectionStrings__App"] = $"Data Source={DbPath}",
            ["Mail__PickupDirectory"] = MailPickupDirectory,
            ["Sqlite__Path"] = DbPath,
            ["Sqlite__SnapshotDirectory"] = SnapshotDirectory,
            // WebPush__* and Litestream__ReplicaUrl are deliberately unset: both pillars stay off without
            // them, which is itself worth exercising — the app has to start and serve regardless.
        };
}
