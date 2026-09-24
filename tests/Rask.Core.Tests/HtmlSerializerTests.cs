using System.Text;
using Rask.Core.ScopedAssets;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

[Collection("ScopedAssets")]
public partial class HtmlSerializerTests : global::Rask.Core.RaskMarkup
{
    public HtmlSerializerTests()
    {
        ScopedAssetRegistry.InvalidateAll();
        ScopedAssetRegistry.RegisterCss(typeof(CssWrapper), ".x { color: red; }");
        ScopedAssetRegistry.RegisterCss(typeof(ScopedWrapper), ".y { color: blue; }");
    }

    [Fact]
    public void Serializing_text_encodes_its_value()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Text.Value("<x>&"), sb);

        Assert.Equal("&lt;x&gt;&amp;", sb.ToString());
    }

    [Fact]
    public void Serializing_raw_emits_its_value_verbatim()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Raw.Value("<i>raw</i>"), sb);

        Assert.Equal("<i>raw</i>", sb.ToString());
    }

    [Fact]
    public void Serializing_a_doctype_emits_the_doctype_literal()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Doctype, sb);

        Assert.Equal("<!DOCTYPE html>", sb.ToString());
    }

    [Fact]
    public void Serializing_a_fragment_renders_its_children_in_order()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize([Text.Value("a"), Raw.Value("<i>"), Text.Value("b")], sb);

        Assert.Equal("a<i>b", sb.ToString());
    }

    [Fact]
    public void Serializing_an_attribute_with_a_null_value_emits_the_bare_attribute_name()
    {
        var data = new Dictionary<string, string?> { ["flag"] = null };
        var html = new Block { Data = data }.ToHtml();

        Assert.Equal("<block data-flag></block>", html);
    }

    [Fact]
    public void Serializing_an_attribute_with_a_string_value_html_encodes_it()
    {
        var html = new Block { Class = "a\"b<c" }.ToHtml();

        Assert.Equal("<block class=\"a&quot;b&lt;c\"></block>", html);
    }

    [Fact]
    public void With_a_scope_id_set_a_non_shell_tag_is_stamped_with_the_data_attribute()
    {
        var view = new CssWrapper(Div[Text.Value("hi")]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));

        Assert.Contains($"<div data-{scopeId}>hi</div>", html);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("body")]
    [InlineData("html")]
    [InlineData("title")]
    [InlineData("meta")]
    [InlineData("link")]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("base")]
    public void A_shell_tag_is_never_stamped_with_the_scope_id(string tag)
    {
        var view = new CssWrapper(ShellOf(tag));
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));

        Assert.DoesNotContain($"data-{scopeId}", html);
    }

    [Fact]
    public void With_a_null_scope_id_nothing_is_stamped()
    {
        // No LiveRenderContext / no scope id → element has only its own attrs.
        var html = new Block { Class = "tag" }.ToHtml();

        Assert.Equal("<block class=\"tag\"></block>", html);
    }

    [Fact]
    public void A_void_element_with_no_children_self_closes() =>
        Assert.Equal("<void />", new VoidEl().ToHtml());

    [Fact]
    public void A_void_element_with_children_still_self_closes()
    {
        var html = new VoidEl()[Text.Value("ignored")].ToHtml();

        Assert.Equal("<void />", html);
    }

    [Fact]
    public void A_void_element_puts_its_attributes_before_the_self_closer() =>
        Assert.Equal("<void class=\"a\" />", new VoidEl { Class = "a" }.ToHtml());

    [Fact]
    public void The_fallthrough_branch_pushes_the_scope_and_recurses()
    {
        var view = new ScopedWrapper(Div[Text.Value("inner")]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(ScopedWrapper));

        Assert.Contains($"<div data-{scopeId}>inner</div>", html);
    }

    private static Component ShellOf(string tag) => tag switch
    {
        // Head() is framework-managed (no children allowed per RASK019) — its case
        // verifies the no-stamp rule alongside the rest of the shell tags.
        "head" => Head,
        "body" => Body[Text.Value("x")],
        "html" => Html[Text.Value("x")],
        "title" => Title[Text.Value("x")],
        "meta" => Meta,
        "link" => Link,
        "script" => Script[Text.Value("x")],
        "style" => Style[Text.Value("x")],
        "base" => Base,
        _ => throw new ArgumentOutOfRangeException(nameof(tag))
    };

    private sealed class Block : Element
    {
        protected override string TagName => "block";
    }

    private sealed class VoidEl : Element
    {
        protected override string TagName => "void";
        protected override bool SelfClosing => true;
    }

    private sealed class CssWrapper : Component
    {
        private readonly Component _body;
        public CssWrapper(Component body) => _body = body;
        protected override Component? Render() => _body;
    }

    private sealed class ScopedWrapper : Component
    {
        private readonly Component _body;
        public ScopedWrapper(Component body) => _body = body;
        protected override Component? Render() => _body;
    }
}
