using Rask.Generators.External.PackageIslands;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers <see cref="PackageIslandNaming" />: names from a package's TypeScript become valid, readable C#
///     identifiers, and its prose becomes documentation that cannot escape its comment.
/// </summary>
public class PackageIslandNamingTests
{
    [Theory]
    [InlineData("small", "Small")]
    [InlineData("x-large", "XLarge")]
    [InlineData("2xl", "N2Xl")]
    [InlineData("aria-label", "AriaLabel")]
    [InlineData("onClick", "OnClick")]
    [InlineData("h1", "H1")]
    [InlineData("primaryDark", "PrimaryDark")]
    [InlineData("data_value", "DataValue")]
    [InlineData("élan", "Élan")]
    public void A_name_becomes_a_pascal_case_identifier(string raw, string expected)
    {
        Assert.Equal(expected, PackageIslandNaming.Identifier(raw, "Fallback"));
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("")]
    [InlineData("---")]
    public void A_name_with_nothing_usable_falls_back(string raw)
    {
        Assert.Equal("Fallback", PackageIslandNaming.Identifier(raw, "Fallback"));
    }

    [Theory]
    [InlineData("1", "N1")]
    [InlineData("-1", "Minus1")]
    [InlineData("0.5", "N0Point5")]
    [InlineData("1e3", "N1E3")]
    public void A_number_literal_is_spelled_out_so_different_numbers_cannot_collide(string text, string expected)
    {
        Assert.Equal(expected, PackageIslandNaming.EnumMember(text, isNumber: true, "Value"));
    }

    [Fact]
    public void Two_literals_that_collide_get_distinct_members()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var first = PackageIslandNaming.Unique(PackageIslandNaming.EnumMember("small", false, "Value"), taken);
        var second = PackageIslandNaming.Unique(PackageIslandNaming.EnumMember("Small", false, "Value"), taken);

        Assert.Equal("Small", first);
        Assert.Equal("Small2", second);
    }

    [Fact]
    public void An_empty_string_literal_is_a_named_member()
    {
        Assert.Equal("Empty", PackageIslandNaming.EnumMember(string.Empty, isNumber: false, "Value"));
    }

    [Fact]
    public void A_doc_comment_cannot_break_out_of_the_comment()
    {
        // A package's prose reaches generated code. A line break followed by code must not end the comment.
        var summary = PackageIslandNaming.Summary("Line one\n}\nclass Evil {", "fallback", null, "    ");

        foreach (var line in summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.StartsWith("    ///", line, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Line\u0085class Evil {")]
    [InlineData("Line\u2028class Evil {")]
    [InlineData("Line\u2029class Evil {")]
    public void A_unicode_line_terminator_cannot_end_the_comment_either(string doc)
    {
        // C# ends a line at NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR as well as at \r and \n. Left in a doc
        // comment, any of them would close the /// and compile what follows as code.
        var summary = PackageIslandNaming.Summary(doc, "fallback", null, "    ");

        foreach (var line in summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.StartsWith("    ///", line, StringComparison.Ordinal);
        }

        foreach (var terminator in new[] { '\u0085', '\u2028', '\u2029' })
        {
            Assert.DoesNotContain(terminator, summary);
            Assert.DoesNotContain(terminator, PackageIslandNaming.SummaryText(doc, "fallback"));
            Assert.DoesNotContain(terminator, PackageIslandNaming.SingleLine(doc));
        }
    }

    [Fact]
    public void An_ordinary_space_is_not_a_line_break()
    {
        Assert.Equal("one two", PackageIslandNaming.SingleLine("one two"));
        Assert.Equal(3, PackageIslandNaming.Summary("one two", "fallback", null, string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Doc_text_is_xml_escaped_and_code_spans_are_kept()
    {
        var text = PackageIslandNaming.SummaryText("Use `a < b` & <b>bold</b>.", "fallback");

        Assert.Equal("Use <c>a &lt; b</c> &amp; &lt;b&gt;bold&lt;/b&gt;.", text);
    }

    [Fact]
    public void The_summary_text_folds_lines_so_a_single_line_tooltip_stays_single()
    {
        Assert.Equal("first second", PackageIslandNaming.SummaryText("first\r\n\n  second  ", "fallback"));
    }
}
