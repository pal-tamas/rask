using Rask.Core.ScopedAssets;

namespace Rask.Core.Tests.ScopedAssets;

// The conservative CSS minifier removes comments + insignificant whitespace but must never change
// meaning: it only strips whitespace around the self-delimiting { } ; , and leaves everything
// context-sensitive (combinators, calc operators, selector colons, string contents) intact.
public class CssMinifierTests
{
    [Fact]
    public void It_strips_comments()
    {
        Assert.Equal(".a{color:red}", CssMinifier.Minify("/* c */.a{color:red}/* trailing */"));
    }

    [Fact]
    public void It_collapses_whitespace_and_indentation()
    {
        const string input = ".a {\n    color: red;\n    margin: 0;\n}\n";

        Assert.Equal(".a{color: red;margin: 0}", CssMinifier.Minify(input));
    }

    [Fact]
    public void It_strips_space_around_delimiters_and_the_trailing_semicolon_before_a_brace()
    {
        Assert.Equal(".a{color:red}", CssMinifier.Minify(".a { color:red ; }"));
        Assert.Equal(".a,.b{x:1}", CssMinifier.Minify(".a , .b { x:1 }"));
    }

    [Fact]
    public void It_preserves_the_descendant_combinator()
    {
        // Collapsed to a single space, but the space (a combinator) must survive.
        Assert.Equal(".a .b{x:1}", CssMinifier.Minify(".a    .b { x:1 }"));
    }

    [Fact]
    public void It_preserves_child_combinator_spaces()
    {
        // We deliberately do not strip around '>', so this stays safe/valid (not over-minified).
        Assert.Equal(".a > .b{x:1}", CssMinifier.Minify(".a > .b { x:1 }"));
    }

    [Fact]
    public void It_preserves_calc_operator_spaces()
    {
        // The spaces around '-' in calc() are load-bearing — stripping them breaks the value. (The space
        // after ':' is conservatively kept too, since ':' is not a stripped delimiter.)
        Assert.Equal(".a{width: calc(100% - 10px)}", CssMinifier.Minify(".a { width: calc(100% - 10px) ; }"));
    }

    [Fact]
    public void It_does_not_collapse_the_space_before_a_selector_colon()
    {
        // "a :hover" (descendant of any :hover) must NOT become "a:hover" (a that is :hover).
        Assert.Equal("a :hover{x:1}", CssMinifier.Minify("a :hover { x:1 }"));
    }

    [Fact]
    public void It_preserves_string_content_verbatim()
    {
        const string input = ".a { content: \"a ; b { } /* not a comment */\" }";

        Assert.Equal(".a{content: \"a ; b { } /* not a comment */\"}", CssMinifier.Minify(input));
    }

    [Fact]
    public void It_treats_a_comment_as_a_token_separator()
    {
        // A comment between two tokens separates them — it must not join them into one identifier.
        Assert.Equal(".a .b{x:1}", CssMinifier.Minify(".a/**/.b{x:1}"));
    }

    [Fact]
    public void Minifying_is_idempotent()
    {
        const string input = "/* c */.a {\n  color: red;\n  width: calc(1px + 2px);\n}\n.b > .c { x:1 }";
        var once = CssMinifier.Minify(input);

        Assert.Equal(once, CssMinifier.Minify(once));
    }

    [Fact]
    public void Minifying_is_deterministic()
    {
        const string input = ".a { color: red } .b { color: blue }";

        Assert.Equal(CssMinifier.Minify(input), CssMinifier.Minify(input));
    }

    [Fact]
    public void An_unterminated_comment_does_not_throw()
    {
        Assert.Equal(".a{x:1}", CssMinifier.Minify(".a{x:1}/* never closed"));
    }

    [Fact]
    public void Empty_input_gives_empty_output()
    {
        Assert.Equal("", CssMinifier.Minify(""));
    }
}
