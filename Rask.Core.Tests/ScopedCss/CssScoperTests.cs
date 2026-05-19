using Rask.Core.ScopedCss;

namespace Rask.Core.Tests.ScopedCss;

public class CssScoperTests
{
    [Fact]
    public void Rewrite_SingleRule_AppendsScopeAttribute()
    {
        var result = CssScoper.Rewrite(".x { color: red; }", "r-abc");
        Assert.Equal(".x[data-r-abc] { color: red; }", result);
    }

    [Fact]
    public void Rewrite_GlobalWrapper_EmitsInnerSelectorUnscoped()
    {
        // Shell tags (html/body) are never stamped data-r-* by HtmlSerializer, so a
        // scoped `body { ... }` rule would never match. :global() is the opt-out.
        var result = CssScoper.Rewrite(":global(body) { color: red; }", "r-x");
        Assert.Equal("body { color: red; }", result);
    }

    [Fact]
    public void Rewrite_GlobalWrapper_AppliesPerCommaSelector()
    {
        // Each comma-separated selector decides independently. A `:global()` wrap on
        // some-but-not-all is a valid mix.
        var result = CssScoper.Rewrite(":global(html), .scoped { font-size: 16px; }", "r-x");
        Assert.Equal("html, .scoped[data-r-x] { font-size: 16px; }", result);
    }

    [Fact]
    public void Rewrite_GlobalWrapper_HandlesAttributeSelector()
    {
        // `:global([role="button"])` mirrors the user's App.css use-case.
        var result = CssScoper.Rewrite(":global([role=\"button\"]) { touch-action: manipulation; }", "r-x");
        Assert.Equal("[role=\"button\"] { touch-action: manipulation; }", result);
    }

    [Fact]
    public void Rewrite_GlobalWrapper_HandlesNestedParens()
    {
        // `:global(.a:not(.b))` keeps the inner paren depth open; the outer `)` is what
        // matches the `:global(` opener.
        var result = CssScoper.Rewrite(":global(.a:not(.b)) { color: red; }", "r-x");
        Assert.Equal(".a:not(.b) { color: red; }", result);
    }

    [Fact]
    public void Rewrite_GlobalWrapper_WithinMediaQuery_StillUnscoped()
    {
        // @media recurses into its body with the same scoping rules; :global() must
        // continue to work inside.
        var result = CssScoper.Rewrite("@media (min-width: 800px) { :global(body) { color: red; } }", "r-x");
        Assert.Contains("@media (min-width: 800px)", result);
        Assert.Contains("body { color: red; }", result);
        Assert.DoesNotContain("body[data-", result);
    }

    [Fact]
    public void Rewrite_PartialGlobalWrapper_DoesNotMatch_FallsBackToScoping()
    {
        // The whole trimmed selector must be `:global(X)`. A trailing combinator
        // disqualifies it — we don't try to handle `:global(body) .foo`-style mixes in v1.
        var result = CssScoper.Rewrite(":global(body) .foo { color: red; }", "r-x");
        // The selector isn't unwrapped; normal scoping applies — suffix on last compound.
        Assert.Contains("[data-r-x]", result);
    }

    [Fact]
    public void Rewrite_CommaSeparatedSelectors_ScopesEachIndependently()
    {
        var result = CssScoper.Rewrite(".a, .b, .c { color: red; }", "r-x");
        Assert.Equal(".a[data-r-x], .b[data-r-x], .c[data-r-x] { color: red; }", result);
    }

    [Fact]
    public void Rewrite_DescendantCombinator_OnlyLastSimpleSelectorScoped()
    {
        var result = CssScoper.Rewrite(".list li { padding: 0; }", "r-x");
        Assert.Equal(".list li[data-r-x] { padding: 0; }", result);
    }

    [Fact]
    public void Rewrite_ChildCombinator_OnlyLastSimpleSelectorScoped()
    {
        var result = CssScoper.Rewrite(".list > li { padding: 0; }", "r-x");
        Assert.Equal(".list > li[data-r-x] { padding: 0; }", result);
    }

    [Fact]
    public void Rewrite_PseudoClass_SuffixGoesBeforePseudo()
    {
        var result = CssScoper.Rewrite(".a:hover { color: red; }", "r-x");
        Assert.Equal(".a[data-r-x]:hover { color: red; }", result);
    }

    [Fact]
    public void Rewrite_PseudoElement_SuffixGoesBeforeDoubleColon()
    {
        var result = CssScoper.Rewrite(".a::before { content: ''; }", "r-x");
        Assert.Equal(".a[data-r-x]::before { content: ''; }", result);
    }

    [Fact]
    public void Rewrite_Media_KeepsAtRulePreludeAndScopesInner()
    {
        var result = CssScoper.Rewrite("@media (min-width: 800px) { .x { color: red; } }", "r-x");
        Assert.Contains("@media (min-width: 800px)", result);
        Assert.Contains(".x[data-r-x] { color: red; }", result);
    }

    [Fact]
    public void Rewrite_Keyframes_LeavesInnerSelectorsUntouched()
    {
        var input = "@keyframes spin { from { opacity: 0; } to { opacity: 1; } }";
        var result = CssScoper.Rewrite(input, "r-x");
        Assert.Equal(input, result);
    }

    [Fact]
    public void Rewrite_FontFace_PassesThrough()
    {
        var input = "@font-face { font-family: 'X'; src: url(/x.woff2); }";
        var result = CssScoper.Rewrite(input, "r-x");
        Assert.Equal(input, result);
    }

    [Fact]
    public void Rewrite_Is_KeepsParensIntact()
    {
        var result = CssScoper.Rewrite(":is(.a, .b) { color: red; }", "r-x");
        Assert.Equal("[data-r-x]:is(.a, .b) { color: red; }", result);
    }

    [Fact]
    public void Rewrite_Comment_StrippedFromOutput()
    {
        var result = CssScoper.Rewrite("/* hi */ .x { color: red; }", "r-x");
        Assert.DoesNotContain("/*", result);
        Assert.Contains(".x[data-r-x]", result);
    }

    [Fact]
    public void Rewrite_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CssScoper.Rewrite("", "r-x"));
        Assert.Equal(string.Empty, CssScoper.Rewrite("   \n  ", "r-x"));
    }

    [Fact]
    public void Rewrite_MultipleRules_AllScoped()
    {
        var result = CssScoper.Rewrite(".a { color: red; } .b { color: blue; }", "r-x");
        Assert.Contains(".a[data-r-x]", result);
        Assert.Contains(".b[data-r-x]", result);
    }

    [Fact]
    public void ScopeIdFor_IsStableAcrossCalls()
    {
        var a = CssScoper.ScopeIdFor(typeof(string));
        var b = CssScoper.ScopeIdFor(typeof(string));
        Assert.Equal(a, b);
        Assert.StartsWith("r-", a);
        Assert.Equal(10, a.Length);
    }

    [Fact]
    public void ScopeIdFor_DifferentTypes_DifferentIds() =>
        Assert.NotEqual(CssScoper.ScopeIdFor(typeof(string)), CssScoper.ScopeIdFor(typeof(int)));
}
