using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite.Crdt.Tests;

public sealed class CrdtConnectionInterceptorTests
{
    [Fact]
    public void Closing_a_connection_without_the_extension_still_closes()
    {
        // The close path runs on every connection the context owns, including ones opened before the
        // extension was configured and ones whose database is already going away. Throwing there would
        // turn an ordinary dispose into an unhandled exception, so a missing crsql_finalize is swallowed.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        CrdtConnectionInterceptor.FinalizeOn(connection);

        Assert.Equal(ConnectionState.Open, connection.State);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void Closing_a_connection_that_is_not_open_is_a_no_op()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        CrdtConnectionInterceptor.FinalizeOn(connection);

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void A_non_sqlite_connection_is_reported_rather_than_skipped()
    {
        var interceptor = new CrdtConnectionInterceptor(new RaskCrdtOptions { ExtensionPath = "crsqlite.dylib" });
        using var connection = new NotSqliteConnection();

        var error = Assert.Throws<InvalidOperationException>(() => interceptor.LoadInto(connection));

        Assert.Contains(nameof(NotSqliteConnection), error.Message, StringComparison.Ordinal);
    }

    /// <summary>Stands in for another provider, or for a connection wrapper around a real SQLite one.</summary>
    private sealed class NotSqliteConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => throw new NotSupportedException();

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
