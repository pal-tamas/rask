// Template — copy to tests/Rask.Core.Tests/Components/{Tag}Tests.cs.
// Asserts exact attribute order: id, class, style, data-*, then tag-specific.
#pragma warning disable RASK014 // tests construct components directly
namespace Rask.Core.Tests.Components;

public class {Tag}Tests
{
    [Fact]
    public void Render_NullProps_EmitsBareTag()
    {
        var html = HtmlSerializer.Serialize(new {Tag}());
        Assert.Equal("<{tag}></{tag}>", html);          // self-closing: "<{tag}>"
    }

    [Fact]
    public void Render_AllPropsSet_EmitsAttributesInOrder()
    {
        var c = new {Tag} { Id = "x", Class = "c", Name = "n", Open = true };
        var html = HtmlSerializer.Serialize(c);
        Assert.Equal("""<{tag} id="x" class="c" name="n" open></{tag}>""", html);
    }
}
