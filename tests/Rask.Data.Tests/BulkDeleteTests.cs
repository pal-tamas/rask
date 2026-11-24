namespace Rask.Data.Tests;

/// <summary>
/// The table an <c>ExecuteDelete</c> statement deletes from, read from the SQL EF Core writes for each provider.
/// </summary>
public sealed class BulkDeleteTests
{
    [Fact]
    public void Sqlites_delete_names_its_table_and_alias()
    {
        const string Sql = "DELETE FROM \"Yard\" AS \"y\"\nWHERE \"y\".\"Name\" = @name";

        var delete = BulkDelete.Parse(Sql);

        Assert.Equal(new BulkDelete(null, "Yard", "\"y\"", "FROM \"Yard\" AS \"y\"\nWHERE \"y\".\"Name\" = @name"), delete);
    }

    [Fact]
    public void Sql_servers_delete_is_read_from_its_second_from()
    {
        const string Sql = "DELETE FROM [y]\r\nFROM [fleet].[Yard] AS [y]\r\nWHERE [y].[Id] IN (SELECT [x].[Id] FROM [Gate] AS [x])";

        var delete = BulkDelete.Parse(Sql)!;

        Assert.Equal(("fleet", "Yard", "[y]"), (delete.Schema, delete.Table, delete.Qualifier));
    }

    [Fact]
    public void Sql_servers_delete_is_read_past_the_row_count_setting_it_opens_with()
    {
        const string Sql = "SET NOCOUNT OFF;\r\nDELETE FROM [q]\r\nFROM [Quay] AS [q]\r\nWHERE [q].[Id] = @id";

        var delete = BulkDelete.Parse(Sql)!;

        Assert.Equal((null, "Quay", "[q]"), (delete.Schema, delete.Table, delete.Qualifier));
        Assert.StartsWith("FROM [Quay] AS [q]", delete.From, StringComparison.Ordinal);
    }

    [Fact]
    public void Postgresqls_unquoted_alias_is_kept_as_written()
    {
        const string Sql = "DELETE FROM referenced.\"Truck\" AS t\nWHERE t.\"Id\" = @p;";

        var delete = BulkDelete.Parse(Sql)!;

        Assert.Equal(("referenced", "Truck", "t"), (delete.Schema, delete.Table, delete.Qualifier));
        Assert.EndsWith("@p", delete.From, StringComparison.Ordinal);
    }

    [Fact]
    public void A_delete_with_no_alias_is_qualified_by_its_table()
    {
        const string Sql = "DELETE FROM \"Yard\"";

        var delete = BulkDelete.Parse(Sql)!;

        Assert.Equal(("Yard", "\"Yard\""), (delete.Table, delete.Qualifier));
    }

    [Theory]
    [InlineData("UPDATE \"Yard\" SET \"Name\" = 'x'")]
    [InlineData("SELECT 1 FROM \"Yard\" AS \"y\"")]
    public void A_statement_that_is_no_delete_is_not_read(string sql)
    {
        var delete = BulkDelete.Parse(sql);

        Assert.Null(delete);
    }
}
