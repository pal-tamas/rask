namespace Rask.Core.Tests.Components;

public class StyleTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<style></style>", Style().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        // `title` is the ordinary global attribute (on <style> it names an alternative stylesheet), so it
        // is inherited from Element and renders in the global group after style — not among <style>'s own
        // type/media/nonce, where it used to sit as a redeclared property.
        Assert.Equal(
            "<style id=\"i\" class=\"c\" style=\"s\" title=\"main\" data-k=\"v\" type=\"text/css\" media=\"all\" nonce=\"abc\"></style>",
            Style("text/css", "all", "abc", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" },
                    Title: "main")
                .ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<style>&lt;x&gt;</style>", Style()["<x>"].ToHtml());
}
