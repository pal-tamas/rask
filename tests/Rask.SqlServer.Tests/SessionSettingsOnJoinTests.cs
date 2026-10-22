using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Rask.SqlServer.Tests;

/// <summary>
///     A context that joins <c>Db.Transaction</c> as it opens is pointed at the transaction's connection, and EF still
///     reports the open: the session settings are not sent again there — that connection got them when it opened, and
///     SqlClient refuses a command without the transaction on a connection in a local transaction.
/// </summary>
public sealed class SessionSettingsOnJoinTests
{
    private const string ConnectionString = "Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true";

    [Fact]
    public async Task An_open_reported_for_a_context_already_in_a_transaction_sends_no_settings()
    {
        await using var db = new PlainContext(Options());
        var shared = new OpenConnection();
        db.Database.SetDbConnection(shared);
        await db.Database.UseTransactionAsync(new SharedTransaction(shared), TestContext.Current.CancellationToken);
        var interceptor = new RaskSqlServerConnectionInterceptor();

        // A SqlConnection never opened: sending the SET script on it would throw.
        await using var unopened = new SqlConnection(ConnectionString);
        await interceptor.ConnectionOpenedAsync(unopened, Opened(unopened, db), TestContext.Current.CancellationToken);
        interceptor.ConnectionOpened(unopened, Opened(unopened, db));

        Assert.NotNull(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task An_open_reported_for_a_context_in_no_transaction_sends_the_settings()
    {
        await using var db = new PlainContext(Options());
        var interceptor = new RaskSqlServerConnectionInterceptor();
        await using var unopened = new SqlConnection(ConnectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.ConnectionOpenedAsync(unopened, Opened(unopened, db), TestContext.Current.CancellationToken));
    }

    private static ConnectionEndEventData Opened(DbConnection connection, DbContext context) =>
        new(
            eventDefinition: null!, // only logging reads it, and nothing logs here
            static (_, _) => "opened",
            connection,
            context,
            Guid.NewGuid(),
            async: true,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero);

    private static DbContextOptions<PlainContext> Options() =>
        new DbContextOptionsBuilder<PlainContext>()
            .UseRaskSqlServer(new ConfigurationServices(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Rask:ConnectionStrings:App"] = ConnectionString })
                .Build()), s => s.LockTimeout = TimeSpan.FromSeconds(1))
            .Options;

    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    // The transaction's connection, already open: joining opens nothing.
    [SuppressMessage("Major Code Smell", "S1144", Justification = "Members the base demands.")]
    private sealed class OpenConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = SessionSettingsOnJoinTests.ConnectionString;

        public override string Database => "rask";

        public override string DataSource => "localhost";

        public override string ServerVersion => "16.0";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class SharedTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;

        protected override DbConnection DbConnection => connection;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }
}
