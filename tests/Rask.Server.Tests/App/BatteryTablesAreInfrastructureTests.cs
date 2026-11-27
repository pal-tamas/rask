using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>
/// Rask's own tables say <see cref="IInfrastructure" />: a transaction that writes a job, a mail or an outbox row beside
/// the app's aggregate is still about one aggregate (<c>OneAggregatePerTransaction</c>).
/// </summary>
public sealed class BatteryTablesAreInfrastructureTests
{
    public static TheoryData<Type> BatteryTables =>
    [
        typeof(Rask.Background.Job),
        typeof(Rask.Background.RecurringJobState),
        typeof(Rask.Mailing.QueuedMail),
        typeof(Rask.Outbox.OutboxMessage),
        typeof(Rask.Auth.Session),
        typeof(Rask.Auth.Passkey),
        typeof(Rask.Auth.AuthInstanceClaim),
        typeof(Rask.Caching.CacheEntry),
        typeof(Rask.WebPush.PushSubscriber),
        typeof(Rask.Storage.StoredFile),
    ];

    [Theory]
    [MemberData(nameof(BatteryTables))]
    public void Every_table_of_Rask_s_own_says_it_is_infrastructure(Type table)
    {
        var says = typeof(IInfrastructure).IsAssignableFrom(table);

        Assert.True(says, $"{table.Name} does not say IInfrastructure");
    }
}
