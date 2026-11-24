using System.Globalization;

namespace Rask.Data.Tests;

// What a SQL Server collation's name says, read so SQLite and PostgreSQL can compare text the same way.
public sealed class TextCollationTests
{
    [Fact]
    public void A_Hungarian_case_insensitive_accent_sensitive_collation_is_read_as_SQL_Server_reads_it()
    {
        var name = "Hungarian_100_CI_AS";

        var collation = TextCollation.Parse(name);

        Assert.Equal("hu-HU", collation.Culture.Name);
        Assert.Equal(CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth, collation.Options);
        Assert.Equal("hu-HU-x-icu", collation.IcuCollation);
    }

    [Fact]
    public void A_SQL_collation_drops_its_code_page_and_Latin1_General_orders_as_the_invariant_culture()
    {
        var name = "SQL_Latin1_General_CP1_CI_AS";

        var collation = TextCollation.Parse(name);

        Assert.Equal(CultureInfo.InvariantCulture.Name, collation.Culture.Name);
        Assert.Equal("und-x-icu", collation.IcuCollation);
    }

    [Fact]
    public void Accent_kana_and_width_sensitivity_are_read_from_their_flags()
    {
        var name = "Japanese_XJIS_140_CS_AI_KS_WS";

        var options = TextCollation.Parse("French_CS_AI").Options;
        var rejected = Assert.Throws<InvalidOperationException>(() => TextCollation.Parse(name));

        Assert.Equal(CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth, options);
        Assert.Contains("Japanese_XJIS", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_binary_collation_compares_ordinally()
    {
        var name = "Latin1_General_100_BIN2_UTF8";

        var collation = TextCollation.Parse(name);

        Assert.Equal(CompareOptions.Ordinal, collation.Options);
        Assert.True(collation.Compare("a", "B") > 0);
    }

    [Fact]
    public void Text_compares_case_insensitively_in_the_language_s_order()
    {
        var collation = TextCollation.Parse("Hungarian_100_CI_AS");

        var equal = collation.Compare("ÁRVÍZTŰRŐ", "árvíztűrő");
        var accents = collation.Compare("Arvizturo", "Árvíztűrő");
        var letters = collation.Compare("Cu", "Cs");

        Assert.Equal(0, equal);
        Assert.NotEqual(0, accents);
        Assert.True(letters < 0);
    }

    [Fact]
    public void A_language_Rask_cannot_tell_is_refused_with_the_call_that_named_it()
    {
        var name = "Klingon_100_CI_AS";

        var refused = Assert.Throws<InvalidOperationException>(() => TextCollation.Parse(name));

        Assert.Contains("c.OrderTextBy(\"Klingon_100_CI_AS\")", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Hungarian", refused.Message, StringComparison.Ordinal);
    }
}
