using Microsoft.Data.Sqlite;
using Rask.Data;

namespace Rask.Mailing.Tests;

/// <summary>
///     A mail sent inside <c>Db.Transaction</c> is a row in that transaction: nobody sees it before the commit, and a
///     rollback leaves nothing to deliver.
/// </summary>
/// <remarks>
///     On a database in a FILE, of the class's own, the one test in the suite that keeps one. "Nobody sees it" is asked
///     from a second connection while the transaction is open, and only a file answers that the way an app's database
///     does — with the committed state. An in-memory database is shared between connections through a shared cache,
///     whose table locks refuse the read instead (<c>database table is locked</c>).
/// </remarks>
public sealed class MailDbTransactionTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"rask-mail-transaction-{Guid.NewGuid():N}.db");

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _file, Pooling = false }.ToString();

    public void Dispose()
    {
        Db.Reset();

        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_mail_sent_inside_is_queued_only_once_the_work_commits()
    {
        await using var harness = new MailHarness(connectionString: ConnectionString);
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
        await using var harness = new MailHarness(connectionString: ConnectionString);
        Db.Configure(harness.NewContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await harness.Queue.Send(Welcome(), TestContext.Current.CancellationToken);
            throw new InvalidOperationException("sign-up failed");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await harness.CountMailAsync());
    }

    private static Mail.Message Welcome() => Mail.Message.To("ada@example.com").Subject("Welcome").Html("<p>hi</p>");
}
