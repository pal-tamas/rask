using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// Guards the public binding API surface (ExpressionAccessor / BindingHelpers) that custom form-bound
// controls — like the MultiSelect example component — rely on. These types were promoted from internal
// to public; this test fails if they regress to internal or change shape.
public class PublicBindingApiTests
{
    [Fact]
    public void ExpressionAccessor_Parse_ResolvesTargetGetterAndField()
    {
        var m = new Model();
        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => m.Name));

        Assert.Same(m, acc.Target);
        Assert.Equal("Name", acc.PropertyName);
        Assert.Equal(typeof(string), acc.PropertyType);
        Assert.Equal("Ada", acc.Getter());
        Assert.Equal(new FieldIdentifier(m, "Name"), acc.Field);
    }

    [Fact]
    public void ExpressionAccessor_Parse_BindsCollectionProperty()
    {
        var m = new Model();
        var acc = ExpressionAccessor.Parse((Expression<Func<ICollection<string>>>)(() => m.Tags));

        Assert.Same(m.Tags, acc.Getter());
        Assert.Equal("Tags", acc.PropertyName);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("hi", "hi")]
    [InlineData(42, "42")]
    public void BindingHelpers_FormatValue_FormatsCommonValues(object? value, string expected) =>
        Assert.Equal(expected, BindingHelpers.FormatValue(value));

    [Fact]
    public void BindingHelpers_FormatValue_UsesInvariantCulture()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // comma decimal separator
        try
        {
            Assert.Equal("1.5", BindingHelpers.FormatValue(1.5));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void BindingHelpers_ResolveBindingContext_ReturnsNull_WithoutAmbientContext() =>
        // Outside a Form / live render, there's no EditContextScope or LiveRenderContext to resolve.
        Assert.Null(BindingHelpers.ResolveBindingContext(new Model()));

    private sealed class Model
    {
        public string Name { get; set; } = "Ada";
        public List<string> Tags { get; } = [];
    }
}
