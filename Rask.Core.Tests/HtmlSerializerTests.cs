using System.Text;
using Rask.Core.Components;
using Rask.Core.ScopedCss;

namespace Rask.Core.Tests;

[Collection("ScopedCss")]
public class HtmlSerializerTests
{
    public HtmlSerializerTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void Serialize_Text_EncodesValue()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new Text("<x>&"), sb);
        Assert.Equal("&lt;x&gt;&amp;", sb.ToString());
    }

    [Fact]
    public void Serialize_Raw_EmitsValueVerbatim()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new Raw("<i>raw</i>"), sb);
        Assert.Equal("<i>raw</i>", sb.ToString());
    }

    [Fact]
    public void Serialize_Doctype_EmitsDoctypeLiteral()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new Doctype(), sb);
        Assert.Equal("<!DOCTYPE html>", sb.ToString());
    }

    [Fact]
    public void Serialize_Fragment_RendersChildrenInOrder()
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(new Fragment(new Text("a"), new Raw("<i>"), new Text("b")), sb);
        Assert.Equal("a<i>b", sb.ToString());
    }

    [Fact]
    public void Serialize_AttributeWithNullValue_EmitsBareAttributeName()
    {
        var data = new Dictionary<string, string?> { ["flag"] = null };
        var html = new Block(new BlockProps(Data: data)).ToHtml();
        Assert.Equal("<block data-flag></block>", html);
    }

    [Fact]
    public void Serialize_AttributeWithStringValue_HtmlEncodesValue()
    {
        var html = new Block(new BlockProps("a\"b<c")).ToHtml();
        Assert.Equal("<block class=\"a&quot;b&lt;c\"></block>", html);
    }

    [Fact]
    public void Serialize_ScopeIdSet_NonShellTag_StampsDataAttribute()
    {
        var view = new CssWrapper(new Div(new Div.Props(), new Text("hi")));
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
        var html = new Block(new BlockProps("tag")).ToHtml();
        Assert.Equal("<block class=\"tag\"></block>", html);
    }

    [Fact]
    public void Serialize_VoidElement_NoChildren_EmitsSelfClose() =>
        Assert.Equal("<void />", new VoidEl(null).ToHtml());

    [Fact]
    public void Serialize_VoidElement_WithChildren_StillSelfCloses()
    {
        var html = new VoidEl(null, new Child[] { new Text("ignored") }).ToHtml();
        Assert.Equal("<void />", html);
    }

    [Fact]
    public void Serialize_VoidElement_WithAttrs_AttrsBeforeSelfCloser() =>
        Assert.Equal("<void class=\"a\" />", new VoidEl(new BlockProps("a")).ToHtml());

    [Fact]
    public void Serialize_FallthroughBranch_PushesScope_AndRecurses()
    {
        var view = new ScopedWrapper(new Div(new Div.Props(), new Text("inner")));
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(ScopedWrapper));
        Assert.Contains($"<div data-{scopeId}>inner</div>", html);
    }

    private static Component ShellOf(string tag) => tag switch
    {
        "head" => new Head(new Head.Props(), new Text("x")),
        "body" => new Body(new Body.Props(), new Text("x")),
        "html" => new Html(new Html.Props(), new Text("x")),
        "title" => new Title(new Title.Props(), new Text("x")),
        "meta" => new Meta(new Meta.Props()),
        "link" => new Link(new Link.Props()),
        "script" => new Script(new Script.Props(), new Text("x")),
        "style" => new Style(new Style.Props(), new Text("x")),
        "base" => new Base(new Base.Props()),
        _ => throw new ArgumentOutOfRangeException(nameof(tag))
    };

    private sealed record BlockProps(
        string? Class = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Class: Class, Data: Data);

    private sealed class Block : Component<BlockProps>
    {
        public Block(BlockProps? props, IEnumerable<Child>? children = null) : base(props, children) { }
        public Block(BlockProps? props, params Child[] children) : base(props, children) { }
        protected override string TagName => "block";
    }

    private sealed class VoidEl : Component<BlockProps>
    {
        public VoidEl(BlockProps? props, IEnumerable<Child>? children = null) : base(props, children) { }
        protected override string TagName => "void";
        protected override bool SelfClosing => true;
    }

    private sealed class CssWrapper : Component
    {
        private readonly Component _body;
        public CssWrapper(Component body) => _body = body;
        protected internal override string? Css => ".x { color: red; }";
        public override Component Render() => _body;
    }

    private sealed class ScopedWrapper : Component
    {
        private readonly Component _body;
        public ScopedWrapper(Component body) => _body = body;
        protected internal override string? Css => ".y { color: blue; }";
        public override Component Render() => _body;
    }
}
