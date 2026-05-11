using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class FieldsetTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<fieldset></fieldset>", new Fieldset(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Fieldset.Props(true, "f", "n",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<fieldset id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled form=\"f\" name=\"n\"></fieldset>",
            new Fieldset(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<fieldset>&lt;x&gt;</fieldset>", new Fieldset(null, "<x>").ToHtml());
}
