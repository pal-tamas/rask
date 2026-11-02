namespace Rask.Data;

/// <summary>The EF Core provider names Rask tells apart, as <c>Database.ProviderName</c> reports them.</summary>
internal static class ProviderNames
{
    internal const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

    internal const string Postgres = "Npgsql.EntityFrameworkCore.PostgreSQL";

    internal const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";
}
