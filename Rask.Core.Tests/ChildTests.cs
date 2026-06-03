namespace Rask.Core.Tests;

public class ChildTests
{
    [Fact]
    public void ImplicitConversion_FromComponent_StoresOriginalComponent()
    {
        var raw = Raw("<b/>");
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
        var raw = Raw("y");
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

    [Fact]
    public void ImplicitConversion_FromInt_RendersDecimalText()
    {
        Child child = 42;
        Assert.IsType<Text>(child.Component);
        Assert.Equal("42", child.Component.ToHtml());
    }

    [Fact]
    public void ImplicitConversion_FromDouble_UsesInvariantCulture()
    {
        var original = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            // A comma-decimal culture would render "1,5" if ToString ignored the provider.
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            Child child = 1.5;
            Assert.Equal("1.5", child.Component.ToHtml());
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void ImplicitConversion_FromChar_RendersTheCharacterNotItsCodePoint()
    {
        Child child = 'A';
        Assert.Equal("A", child.Component.ToHtml());
    }

    [Fact]
    public void ImplicitConversion_FromBool_RendersTrueFalse()
    {
        Child t = true;
        Child f = false;
        Assert.Equal("True", t.Component.ToHtml());
        Assert.Equal("False", f.Component.ToHtml());
    }

    [Fact]
    public void ImplicitConversion_FromDateOnly_UsesInvariantCulture()
    {
        Child child = new DateOnly(2026, 6, 3);
        Assert.Equal("06/03/2026", child.Component.ToHtml());
    }

    [Fact]
    public void ImplicitConversion_FromValue_FlowsThroughElementIndexer()
    {
        // The whole point: no .ToString() at the call site.
        var html = Td()[42].ToHtml();
        Assert.Equal("<td>42</td>", html);
    }
}
