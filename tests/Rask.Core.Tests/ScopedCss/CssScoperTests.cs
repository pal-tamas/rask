using Rask.Core.ScopedCss;

namespace Rask.Core.Tests.ScopedCss;

public class CssScoperTests
{
    [Fact]
    public void Rewriting_a_single_rule_appends_the_scope_attribute()
    {
        var result = CssScoper.Rewrite(".x { color: red; }", "r-abc");

        Assert.Equal(".x[data-r-abc] { color: red; }", result);
    }

    [Fact]
    public void Rewriting_comma_separated_selectors_scopes_each_independently()
    {
        var result = CssScoper.Rewrite(".a, .b, .c { color: red; }", "r-x");

        Assert.Equal(".a[data-r-x], .b[data-r-x], .c[data-r-x] { color: red; }", result);
    }

    [Fact]
    public void Rewriting_a_descendant_combinator_scopes_only_the_last_simple_selector()
    {
        var result = CssScoper.Rewrite(".list li { padding: 0; }", "r-x");

        Assert.Equal(".list li[data-r-x] { padding: 0; }", result);
    }

    [Fact]
    public void Rewriting_a_child_combinator_scopes_only_the_last_simple_selector()
    {
        var result = CssScoper.Rewrite(".list > li { padding: 0; }", "r-x");

        Assert.Equal(".list > li[data-r-x] { padding: 0; }", result);
    }

    [Fact]
    public void Rewriting_a_pseudo_class_puts_the_suffix_before_the_pseudo()
    {
        var result = CssScoper.Rewrite(".a:hover { color: red; }", "r-x");

        Assert.Equal(".a[data-r-x]:hover { color: red; }", result);
    }

    [Fact]
    public void Rewriting_a_pseudo_element_puts_the_suffix_before_the_double_colon()
    {
        var result = CssScoper.Rewrite(".a::before { content: ''; }", "r-x");

        Assert.Equal(".a[data-r-x]::before { content: ''; }", result);
    }

    [Fact]
    public void Rewriting_media_keeps_the_at_rule_prelude_and_scopes_the_inner_rules()
    {
        var result = CssScoper.Rewrite("@media (min-width: 800px) { .x { color: red; } }", "r-x");

        Assert.Contains("@media (min-width: 800px)", result);
        Assert.Contains(".x[data-r-x] { color: red; }", result);
    }

    [Fact]
    public void Rewriting_keyframes_leaves_the_inner_selectors_untouched()
    {
        var input = "@keyframes spin { from { opacity: 0; } to { opacity: 1; } }";
        var result = CssScoper.Rewrite(input, "r-x");

        Assert.Equal(input, result);
    }

    [Fact]
    public void Rewriting_passes_font_face_through()
    {
        var input = "@font-face { font-family: 'X'; src: url(/x.woff2); }";
        var result = CssScoper.Rewrite(input, "r-x");

        Assert.Equal(input, result);
    }

    [Fact]
    public void Rewriting_an_is_selector_keeps_its_parens_intact()
    {
        var result = CssScoper.Rewrite(":is(.a, .b) { color: red; }", "r-x");

        Assert.Equal("[data-r-x]:is(.a, .b) { color: red; }", result);
    }

    [Fact]
    public void Rewriting_strips_a_comment_from_the_output()
    {
        var result = CssScoper.Rewrite("/* hi */ .x { color: red; }", "r-x");

        Assert.DoesNotContain("/*", result);
        Assert.Contains(".x[data-r-x]", result);
    }

    [Fact]
    public void Rewriting_empty_input_gives_empty_output()
    {
        Assert.Equal(string.Empty, CssScoper.Rewrite("", "r-x"));
        Assert.Equal(string.Empty, CssScoper.Rewrite("   \n  ", "r-x"));
    }

    [Fact]
    public void Rewriting_multiple_rules_scopes_them_all()
    {
        var result = CssScoper.Rewrite(".a { color: red; } .b { color: blue; }", "r-x");

        Assert.Contains(".a[data-r-x]", result);
        Assert.Contains(".b[data-r-x]", result);
    }

    [Fact]
    public void The_scope_id_is_stable_across_calls()
    {
        var a = CssScoper.ScopeIdFor(typeof(string));
        var b = CssScoper.ScopeIdFor(typeof(string));

        Assert.Equal(a, b);
        Assert.StartsWith("r-", a);
        Assert.Equal(10, a.Length);
    }

    [Fact]
    public void Different_types_get_different_scope_ids() =>
        Assert.NotEqual(CssScoper.ScopeIdFor(typeof(string)), CssScoper.ScopeIdFor(typeof(int)));
}
