namespace Rask.Core.Tests.Components;

public class FieldsetTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<fieldset></fieldset>", Fieldset().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<fieldset id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled form=\"f\" name=\"n\"></fieldset>",
            Fieldset(true, "f", "n", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<fieldset>&lt;x&gt;</fieldset>", Fieldset()["<x>"].ToHtml());
}
