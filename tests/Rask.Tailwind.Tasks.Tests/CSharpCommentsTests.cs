namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     A C# source with its comments taken out, for Tailwind to scan: prose is words, and a word that is a
///     class name became a rule in the kit's sheet (#1214). The code has to survive byte for byte.
/// </summary>
public sealed class CSharpCommentsTests
{
    [Fact]
    public void A_doc_comment_is_taken_out_and_the_code_under_it_is_kept()
    {
        var source = "/// <summary>A magnifying glass when unset.</summary>\npublic string Icon { get; set; } = \"size-4\";\n";

        var stripped = CSharpComments.Strip(source);

        Assert.Equal("\npublic string Icon { get; set; } = \"size-4\";\n", stripped);
    }

    [Fact]
    public void A_block_comment_keeps_its_line_breaks_and_a_space_keeps_the_words_either_side_apart()
    {
        var source = "var a = \"flex\";/* glass\n   card */var b = \"grid\";\nvar c/*x*/= 1;";

        var stripped = CSharpComments.Strip(source);

        Assert.Equal("var a = \"flex\";\nvar b = \"grid\";\nvar c = 1;", stripped);
    }

    [Theory]
    [InlineData("var u = \"https://rask.sh/docs\";")]
    [InlineData("var p = \"/* not a comment */\";")]
    [InlineData("var v = @\"C:\\path\\\"\"quoted\"\" // still text\";")]
    [InlineData("var r = \"\"\"\n  a \"quote\" and // text and \"\" two\n  \"\"\";")]
    [InlineData("var e = \"\";var q = '\"';var s = '\\'';var t = \"//\";")]
    [InlineData("var i = $\"{(on ? \"btn-primary\" : \"btn-ghost\")} // text {{braces}}\";")]
    [InlineData("var j = $$\"\"\"{ \"literal\" } {{Size}} // text\"\"\";")]
    [InlineData("var k = $@\"{x}\"\" // text\";var l = @$\"{y} /* text */\";")]
    public void A_comment_marker_inside_a_literal_is_text_not_a_comment(string source)
    {
        var stripped = CSharpComments.Strip(source);

        Assert.Equal(source, stripped);
    }

    [Fact]
    public void A_comment_inside_an_interpolations_hole_is_taken_out()
    {
        var source = "var i = $\"{Size /* glass */} px\"; // card";

        var stripped = CSharpComments.Strip(source);

        Assert.Equal("var i = $\"{Size  } px\"; ", stripped);
    }

    [Fact]
    public void A_line_comment_after_a_string_with_braces_in_a_hole_is_still_found()
    {
        var source = "var i = $\"{new[] { \"a\" }.Length}\"; // glass\nvar j = 1;";

        var stripped = CSharpComments.Strip(source);

        Assert.Equal("var i = $\"{new[] { \"a\" }.Length}\"; \nvar j = 1;", stripped);
    }
}
