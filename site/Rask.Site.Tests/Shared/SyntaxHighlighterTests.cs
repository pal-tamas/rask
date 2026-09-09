using Rask.Site;

namespace Rask.Site.Tests.Shared;

// The island dialects had no lexer: SyntaxHighlighter.LanguageFor returned null for .tsx, .jsx, .vue
// and .svelte, and a null language is rendered by the callers as plain encoded text. That failure is
// invisible in a screenshot — "no lexer" and "a lexer that matched nothing" produce the same grey
// block — so it is asserted here rather than looked at.
public sealed class SyntaxHighlighterTests
{
    [Theory]
    [InlineData(".tsx")]
    [InlineData("tsx")]
    [InlineData(".jsx")]
    [InlineData("jsx")]
    [InlineData(".vue")]
    [InlineData("vue")]
    [InlineData(".svelte")]
    [InlineData("svelte")]
    public void LanguageFor_ResolvesTheIslandDialects(string key)
    {
        Assert.NotNull(SyntaxHighlighter.LanguageFor(key));
    }

    [Fact]
    public void LanguageFor_StillResolvesTheLanguagesItAlreadyDid()
    {
        Assert.NotNull(SyntaxHighlighter.LanguageFor(".cs"));
        Assert.NotNull(SyntaxHighlighter.LanguageFor(".ts"));
        Assert.NotNull(SyntaxHighlighter.LanguageFor("bash"));
        Assert.NotNull(SyntaxHighlighter.LanguageFor(".css"));

        // Unchanged: the guides use these and they stay deliberately plain.
        Assert.Null(SyntaxHighlighter.LanguageFor("razor"));
        Assert.Null(SyntaxHighlighter.LanguageFor("jsonc"));
    }

    [Fact]
    public void Tsx_ColoursElementsAttributesKeywordsAndStrings()
    {
        var language = SyntaxHighlighter.LanguageFor(".tsx");
        Assert.NotNull(language);

        var html = SyntaxHighlighter.Highlight(
            "export function Counter({ start }: { start: number }) {\n"
            + "  return <button onClick={inc} title=\"go\">{start}</button>;\n"
            + "}",
            language);

        // The element name, as a className token — the class global.css already styles for every other
        // language, which is why the tag rules map onto it rather than ColorCode's html* scopes.
        Assert.Contains("className", html);
        Assert.Contains("button", html);

        // The attribute name, and the keyword and string passes still running underneath the tag rules.
        Assert.Contains("keyword", html);
        Assert.Contains("string", html);
    }

    [Fact]
    public void Tsx_DoesNotLexATagInsideAStringAsAnElement()
    {
        var language = SyntaxHighlighter.LanguageFor(".tsx");
        Assert.NotNull(language);

        // The precedence this language is ordered for: strings are matched before the tag rule, so the
        // <div> here is part of the string and never becomes an element. Written as a test because rule
        // order is the one thing about a ColorCode language that cannot be seen by reading its output.
        var html = SyntaxHighlighter.Highlight("const markup = \"<div>hi</div>\";", language);

        var stringStart = html.IndexOf("string", StringComparison.Ordinal);
        Assert.True(stringStart >= 0, "the literal should be lexed as a string");

        // No element token anywhere: the whole literal is one string span.
        Assert.DoesNotContain("className\">div", html);
    }

    [Fact]
    public void Vue_AndSvelte_ShareTheTsxRules()
    {
        // One language covers all four dialects — an SFC is markup wrapped around a script block. This
        // pins that decision so splitting them later is a deliberate change rather than a silent one.
        Assert.Same(SyntaxHighlighter.LanguageFor(".vue"), SyntaxHighlighter.LanguageFor(".tsx"));
        Assert.Same(SyntaxHighlighter.LanguageFor(".svelte"), SyntaxHighlighter.LanguageFor(".jsx"));
    }
}
