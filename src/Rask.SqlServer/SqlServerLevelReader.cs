using Microsoft.Data.SqlClient;

namespace Rask.SqlServer;

/// <summary>
/// Reads the compatibility level off the server itself: the database's own when it exists, otherwise the one the
/// server's version gives a new database.
/// </summary>
/// <remarks>
/// <para>
/// One unpooled connection per read, so the probe leaves no idle connection behind in a pool of its own, and a short
/// connect timeout, so an offline <c>dotnet ef</c> or a host whose server is still starting waits seconds, not the
/// connection string's full timeout.
/// </para>
/// <para>
/// A database that does not exist yet — the first <c>migrate</c>, a test run's fresh database — is read from
/// <c>master</c> instead, by the server's major version: SQL Server 2025 (17) is 170, 2022 (16) is 160, 2019 and Azure
/// SQL Edge (15) are 150. Azure SQL Database reports 12 whatever it runs, so anything below 13 maps to nothing and EF
/// Core keeps its own default.
/// </para>
/// </remarks>
internal sealed class SqlServerLevelReader : ISqlServerLevelReader
{
    /// <summary>The longest the probe waits for a connection, in seconds.</summary>
    internal const int ConnectTimeoutSeconds = 5;

    // "Cannot open database requested by the login": the server is up, the database is not there yet.
    private const int CannotOpenDatabase = 4060;

    private const string DatabaseLevel = "SELECT CAST(compatibility_level AS int) FROM sys.databases WHERE name = DB_NAME();";

    private const string ServerMajorVersion = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int);";

    public int? Read(string connectionString)
    {
        var probe = new SqlConnectionStringBuilder(connectionString) { Pooling = false };

        // 0 is "wait forever", which is exactly what a probe must not do.
        if (probe.ConnectTimeout is <= 0 or > ConnectTimeoutSeconds)
        {
            probe.ConnectTimeout = ConnectTimeoutSeconds;
        }

        try
        {
            return Scalar(probe.ConnectionString, DatabaseLevel);
        }
        catch (SqlException ex) when (ex.Number == CannotOpenDatabase)
        {
            probe.InitialCatalog = "master";
            return FromMajorVersion(Scalar(probe.ConnectionString, ServerMajorVersion));
        }
    }

    /// <summary>The level a new database gets on a server of <paramref name="majorVersion"/>, when Rask knows it.</summary>
    internal static int? FromMajorVersion(int? majorVersion) => majorVersion >= 13 ? majorVersion * 10 : null;

    private static int? Scalar(string connectionString, string query)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = query;
        return command.ExecuteScalar() is int value ? value : null;
    }
}
