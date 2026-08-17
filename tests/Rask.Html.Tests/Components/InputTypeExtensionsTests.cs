namespace Rask.Html.Tests.Components;

// InputType.ToHtml() is the single source of truth for the HTML `type` token (the analyzer RASK025 and
// the input-type derivation both lean on it). Pin every member's token so a rename or a dropped switch
// arm can't silently regress to the "text" fallback.
public class InputTypeExtensionsTests
{
    [Theory]
    [InlineData(InputType.Text, "text")]
    [InlineData(InputType.Search, "search")]
    [InlineData(InputType.Tel, "tel")]
    [InlineData(InputType.Url, "url")]
    [InlineData(InputType.Email, "email")]
    [InlineData(InputType.Password, "password")]
    [InlineData(InputType.Number, "number")]
    [InlineData(InputType.Checkbox, "checkbox")]
    [InlineData(InputType.Radio, "radio")]
    [InlineData(InputType.File, "file")]
    [InlineData(InputType.Range, "range")]
    [InlineData(InputType.Color, "color")]
    [InlineData(InputType.Date, "date")]
    [InlineData(InputType.DatetimeLocal, "datetime-local")]
    [InlineData(InputType.Time, "time")]
    [InlineData(InputType.Week, "week")]
    [InlineData(InputType.Month, "month")]
    [InlineData(InputType.Hidden, "hidden")]
    [InlineData(InputType.Button, "button")]
    [InlineData(InputType.Submit, "submit")]
    [InlineData(InputType.Reset, "reset")]
    [InlineData(InputType.Image, "image")]
    public void ToHtml_MapsEveryMemberToItsHtmlToken(InputType type, string expected) =>
        Assert.Equal(expected, type.ToHtml());

    [Fact]
    public void ToHtml_CoversEveryEnumMember_WithoutFallingBackToText()
    {
        // Only InputType.Text is allowed to yield "text"; any other member doing so means it slipped through
        // the switch to the default arm (i.e. someone added an enum value but forgot to map it).
        foreach (var type in Enum.GetValues<InputType>())
        {
            var html = type.ToHtml();
            Assert.False(string.IsNullOrEmpty(html));
            if (type != InputType.Text)
            {
                Assert.NotEqual("text", html);
            }
        }
    }
}
