using System.Text;
using Rask.Core.Components;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

[Collection("ScopedCss")]
public class HtmlSerializerTests
{
    public HtmlSerializerTests()
    {
        ScopedCssRegistry.InvalidateAll();
        ScopedCssRegistry.RegisterType(typeof(CssWrapper), ".x { color: red; }");
        ScopedCssRegistry.RegisterType(typeof(ScopedWrapper), ".y { color: blue; }");
    }

    [Fact]
    public void Serialize_Text_EncodesValue()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Text("<x>&"), sb);
        Assert.Equal("&lt;x&gt;&amp;", sb.ToString());
    }

    [Fact]
    public void Serialize_Raw_EmitsValueVerbatim()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Raw("<i>raw</i>"), sb);
        Assert.Equal("<i>raw</i>", sb.ToString());
    }

    [Fact]
    public void Serialize_Doctype_EmitsDoctypeLiteral()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Doctype(), sb);
        Assert.Equal("<!DOCTYPE html>", sb.ToString());
    }

    [Fact]
    public void Serialize_Fragment_RendersChildrenInOrder()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(Fragment()[Text("a"), Raw("<i>"), Text("b")], sb);
        Assert.Equal("a<i>b", sb.ToString());
    }

    [Fact]
    public void Serialize_AttributeWithNullValue_EmitsBareAttributeName()
    {
        var data = new Dictionary<string, string?> { ["flag"] = null };
        var html = new Block { Data = data }.ToHtml();
        Assert.Equal("<block data-flag></block>", html);
    }

    [Fact]
    public void Serialize_AttributeWithStringValue_HtmlEncodesValue()
    {
        var html = new Block { Class = "a\"b<c" }.ToHtml();
        Assert.Equal("<block class=\"a&quot;b&lt;c\"></block>", html);
    }

    [Fact]
    public void Serialize_ScopeIdSet_NonShellTag_StampsDataAttribute()
    {
        var view = new CssWrapper(Div()[Text("hi")]);
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
    public void Serialize_ScopeIdSet_ShellTag_DoesNotStamp(string tag)
    {
        var view = new CssWrapper(ShellOf(tag));
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));
        Assert.DoesNotContain($"data-{scopeId}", html);
    }

    [Fact]
    public void Serialize_ScopeIdNull_NoStamping()
    {
        // No LiveRenderContext / no scope id → element has only its own attrs.
        var html = new Block { Class = "tag" }.ToHtml();
        Assert.Equal("<block class=\"tag\"></block>", html);
    }

    [Fact]
    public void Serialize_VoidElement_NoChildren_EmitsSelfClose() =>
        Assert.Equal("<void />", new VoidEl().ToHtml());

    [Fact]
    public void Serialize_VoidElement_WithChildren_StillSelfCloses()
    {
        var html = new VoidEl()[Text("ignored")].ToHtml();
        Assert.Equal("<void />", html);
    }

    [Fact]
    public void Serialize_VoidElement_WithAttrs_AttrsBeforeSelfCloser() =>
        Assert.Equal("<void class=\"a\" />", new VoidEl { Class = "a" }.ToHtml());

    [Fact]
    public void Serialize_FallthroughBranch_PushesScope_AndRecurses()
    {
        var view = new ScopedWrapper(Div()[Text("inner")]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(ScopedWrapper));
        Assert.Contains($"<div data-{scopeId}>inner</div>", html);
    }

    private static Component ShellOf(string tag) => tag switch
    {
        // Head() is framework-managed (no children allowed per RASK019) — its case
        // verifies the no-stamp rule alongside the rest of the shell tags.
        "head" => Head(),
        "body" => Body()[Text("x")],
        "html" => Html()[Text("x")],
        "title" => Title()[Text("x")],
        "meta" => Meta(),
        "link" => Link(),
        "script" => Script()[Text("x")],
        "style" => Style()[Text("x")],
        "base" => Base(),
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
        protected override Component Render() => _body;
    }

    private sealed class ScopedWrapper : Component
    {
        private readonly Component _body;
        public ScopedWrapper(Component body) => _body = body;
        protected override Component Render() => _body;
    }
}
