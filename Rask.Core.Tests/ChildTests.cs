namespace Rask.Core.Tests;

public class ChildTests
{
    [Fact]
    public void ImplicitConversion_FromComponent_StoresOriginalComponent()
    {
        var raw = new Raw("<b/>");
        Child child = raw;
        Assert.Same(raw, child.Component);
    }

    [Fact]
    public void ImplicitConversion_FromString_WrapsAsTextNode()
    {
        Child child = "<x>";
        Assert.IsType<Text>(child.Component);
        Assert.Equal("&lt;x&gt;", child.Component.ToHtml());
    }

    [Fact]
    public void ConstructorWithComponent_ExposesSameInstance()
    {
        var raw = new Raw("y");
        var child = new Child(raw);
        Assert.Same(raw, child.Component);
    }

    [Fact]
    public void ConstructorWithString_ProducesEncodedTextOnRender()
    {
        var child = new Child("<x>");
        Assert.IsType<Text>(child.Component);
        Assert.Equal("&lt;x&gt;", child.Component.ToHtml());
    }
}
