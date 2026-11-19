namespace Rask.UiTests.Components;

/// <summary>
///     An app's own editor extension registers the markup it writes, so the server-side cut keeps it — and nothing a
///     registration says lets script back in. Registration is process-wide, so each test names tags of its own.
/// </summary>
public class UiEditorSchemaTests
{
    [Fact]
    public void A_registered_tag_keeps_its_registered_attributes_and_loses_the_rest()
    {
        UiEditorSchema.Allow("x-chip", "data-id", "title");

        var kept = UiEditorHtml.Clean("<p><x-chip data-id=\"7\" title=\"Ada\" onclick=\"alert(1)\" class=\"c\">Ada</x-chip></p>");

        Assert.Equal("<p><x-chip data-id=\"7\" title=\"Ada\">Ada</x-chip></p>", kept);
    }

    [Fact]
    public void A_tag_nobody_registered_is_still_dropped()
    {
        UiEditorSchema.Allow("x-known", "data-id");

        var kept = UiEditorHtml.Clean("<p><x-unknown data-id=\"7\">text</x-unknown></p>");

        Assert.Equal("<p>text</p>", kept);
    }

    [Fact]
    public void A_registered_attribute_s_rule_decides_its_value()
    {
        UiEditorSchema.Allow("x-mention", "data-user", value => value.All(char.IsAsciiDigit));

        var kept = UiEditorHtml.Clean("<x-mention data-user=\"12\">a</x-mention><x-mention data-user=\"ab\">b</x-mention>");

        Assert.Equal("<x-mention data-user=\"12\">a</x-mention><x-mention>b</x-mention>", kept);
    }

    [Theory]
    [InlineData("script", "src")]
    [InlineData("iframe", "src")]
    [InlineData("svg", "viewbox")]
    [InlineData("style", "media")]
    [InlineData("form", "action")]
    [InlineData("x-safe", "onclick")]
    [InlineData("x-safe", "ONLOAD")]
    [InlineData("x-safe", "style")]
    [InlineData("x-safe", "srcdoc")]
    [InlineData("x-safe", "srcset")]
    [InlineData("x safe", "title")]
    public void A_registration_that_would_let_script_back_in_is_refused(string tag, string attribute)
    {
        var refused = Record.Exception(() => UiEditorSchema.Allow(tag, attribute));

        Assert.IsType<ArgumentException>(refused);
    }

    [Fact]
    public void A_registered_url_attribute_is_still_held_to_a_safe_link()
    {
        UiEditorSchema.Allow("x-link", "href");
        UiEditorSchema.Allow("x-pic", "src", _ => true);

        var kept = UiEditorHtml.Clean(
            "<x-link href=\"javascript:alert(1)\">a</x-link><x-link href=\"https://rask.sh/\">b</x-link>"
            + "<x-pic src=\"data:image/png;base64,AAAA\"></x-pic><x-pic src=\"data:image/svg+xml,<svg onload=alert(1)>\"></x-pic>"
            + "<x-pic src=\" JaVa&#9;ScRiPt:alert(1)\"></x-pic>");

        Assert.Equal(
            "<x-link>a</x-link><x-link href=\"https://rask.sh/\">b</x-link>"
            + "<x-pic src=\"data:image/png;base64,AAAA\"></x-pic><x-pic></x-pic><x-pic></x-pic>",
            kept);
    }

    [Fact]
    public void Registering_an_attribute_the_kit_already_rules_does_not_loosen_its_rule()
    {
        UiEditorSchema.Allow("a", "href", _ => true);

        var kept = UiEditorHtml.Clean("<p><a href=\"javascript:alert(1)\">x</a></p>");

        Assert.Equal("<p><a>x</a></p>", kept);
    }
}
