using Rask.Data;

namespace Rask.Mailing.Tests;

/// <summary>
///     A mail sent inside <c>Db.Transaction</c> is a row in that transaction: nobody sees it before the commit, and a
///     rollback leaves nothing to deliver.
/// </summary>
[Collection(MailDbCollection.Name)]
public sealed class MailDbTransactionTests : IDisposable
{
    public void Dispose() => Db.Reset();

    [Fact]
    public async Task A_mail_sent_inside_is_queued_only_once_the_work_commits()
    {
        await using var harness = new MailHarness();
        Db.Configure(harness.NewContext);
        var seenBeforeCommit = -1;

        await Db.Transaction(async () =>
        {
            await harness.Queue.Send(Welcome(), TestContext.Current.CancellationToken);
            seenBeforeCommit = await harness.CountMailAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, seenBeforeCommit);
        Assert.Equal(1, await harness.CountMailAsync());
    }

    [Fact]
    public async Task A_rollback_leaves_no_mail_to_deliver()
    {
        await using var harness = new MailHarness();
        Db.Configure(harness.NewContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await harness.Queue.Send(Welcome(), TestContext.Current.CancellationToken);
            throw new InvalidOperationException("sign-up failed");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await harness.CountMailAsync());
    }

    private static Email Welcome() => Email.To("ada@example.com").Subject("Welcome").Html("<p>hi</p>");
}
