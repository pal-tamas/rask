using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite.Tests;

// The comparison OrderTextBy("Hungarian_100_CI_AS") registers: RASK_TEXT, and instr and LIKE compared by it.
public sealed class SqliteTextOrderTests
{
    private const CompareOptions CaseInsensitive = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    private static readonly CompareInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU").CompareInfo;

    [Theory]
    [InlineData("Árvíztűrő", "árv%", true)]
    [InlineData("Árvíztűrő", "%TŰRŐ", true)]
    [InlineData("Árvíztűrő", "%víz%", true)]
    [InlineData("Árvíztűrő", "Arv%", false)]
    [InlineData("Árvíztűrő", "Á_víztűrő", true)]
    [InlineData("Árvíztűrő", "Á_ztűrő", false)]
    [InlineData("Árvíztűrő", "%%%ő", true)]
    [InlineData("", "%", true)]
    [InlineData("", "_", false)]
    [InlineData("abc", "", false)]
    public void A_pattern_matches_as_the_culture_compares(string text, string pattern, bool expected)
    {
        var compare = Hungarian;

        var matched = SqliteLikePattern.IsMatch(text, pattern, escape: null, compare, CaseInsensitive);

        Assert.Equal(expected, matched);
    }

    [Theory]
    [InlineData("50%_off", "50\\%\\_%", true)]
    [InlineData("500 off", "50\\%\\_%", false)]
    [InlineData("a\\b", "a\\\\b", true)]
    public void An_escaped_wildcard_matches_itself_alone(string text, string pattern, bool expected)
    {
        var escape = '\\';

        var matched = SqliteLikePattern.IsMatch(text, pattern, escape, Hungarian, CaseInsensitive);

        Assert.Equal(expected, matched);
    }

    [Fact]
    public void One_wildcard_character_is_one_character_even_outside_the_basic_plane()
    {
        var text = "a😀b";

        var matched = SqliteLikePattern.IsMatch(text, "a_b", escape: null, Hungarian, CaseInsensitive);

        Assert.True(matched);
    }

    [Fact]
    public void A_binary_comparison_keeps_case_apart()
    {
        var invariant = CultureInfo.InvariantCulture.CompareInfo;

        var matched = SqliteLikePattern.IsMatch("Abc", "abc", escape: null, invariant, CompareOptions.Ordinal);

        Assert.False(matched);
    }

    [Fact]
    public async Task On_a_connection_instr_LIKE_and_the_collation_compare_by_the_culture()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        SqliteTextOrder.Apply(connection, Hungarian, CaseInsensitive);

        var answers = await Scalar(connection,
            "SELECT instr('Árvíztűrő Tükörfúrógép', 'TÜKÖR') || ',' || ('Árvíztűrő' LIKE 'árv%') || ',' "
            + "|| ('50%' LIKE '50\\%' ESCAPE '\\') || ',' || ('ÁRVÍZ' = 'árvíz' COLLATE RASK_TEXT) || ',' "
            + "|| ('Cs' < 'Cu' COLLATE RASK_TEXT) || ',' || coalesce(instr(NULL, 'a'), 'null')");

        Assert.Equal("11,1,1,1,0,null", answers);
    }

    [Fact]
    public void Null_orders_before_any_text_and_equal_to_itself()
    {
        var compare = Hungarian;

        var order = new[]
        {
            SqliteTextOrder.Compare(compare, CaseInsensitive, null, "a"),
            SqliteTextOrder.Compare(compare, CaseInsensitive, "a", null),
            SqliteTextOrder.Compare(compare, CaseInsensitive, null, null),
        };

        Assert.Equal([-1, 1, 0], order);
    }

    private static async Task<string?> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
    }
}
