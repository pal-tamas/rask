namespace Rask.Ui.Tests.Components;

/// <summary>
///     The panel, the composition of its class attribute, and the linked form of it.
/// </summary>
public partial class UiCardTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_writes_the_kit_panel_classes() =>
        Assert.Contains(UiStyles.Card, UiCard[Span["body"]].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void An_own_class_is_appended() =>
        Assert.Contains(
            UiStyles.Card + " w-full",
            UiCard.Class("w-full")[Span["body"]].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void With_no_own_class_the_attribute_ends_at_the_last_class()
    {
        // Every other component composes with UiClass.Compose; this one interpolated, so an unset Class
        // left `class="… sm:p-5 "` on every card the kit has ever drawn. Harmless to a browser and
        // invisible on a page, but it is a byte of difference in markup two lanes are meant to match
        // exactly, and it shows up in every snapshot and every class-contract assertion.
        var html = UiCard[Span["body"]].ToHtml();

        Assert.DoesNotContain(" \"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_is_omitted_when_there_is_nothing_to_put_in_it() =>
        Assert.DoesNotContain("mb-4 flex", UiCard[Span["body"]].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_heading_renders_in_the_header()
    {
        var html = UiCard.Heading("Orders")[Span["body"]].ToHtml();

        Assert.Contains("<h2", html, StringComparison.Ordinal);
        Assert.Contains("Orders", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_does_not_space_its_body()
    {
        // Every demo result on the site sits in a UiCard. Spacing the body would restyle all of them at once,
        // so the card leaves its children's rhythm to them; only the header carries a margin.
        var html = UiCard.Heading("Orders")[Span["one"], Span["two"]].ToHtml();

        Assert.DoesNotContain("space-y-", html, StringComparison.Ordinal);
        Assert.Contains("mb-4 flex", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_sits_before_the_heading()
    {
        var html = UiCard.Heading("Jobs").Icon(UiIconName.Gear)[Span["body"]].ToHtml();

        var icon = html.IndexOf("size-5 shrink-0 opacity-60", StringComparison.Ordinal);
        Assert.True(icon >= 0, "the icon was not rendered");
        Assert.True(icon < html.IndexOf("<h2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_card_with_an_href_is_one_link_holding_everything()
    {
        // One link, not a link beside a card: the whole panel is the target, so it is one tab stop and one
        // announcement, and "open in new tab" works anywhere on it.
        var html = UiCard.Href("/_rask/queues/jobs").Heading("Jobs")[Span["body"]].ToHtml();

        Assert.StartsWith("<a ", html, StringComparison.Ordinal);
        Assert.EndsWith("</a>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/_rask/queues/jobs\"", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("<a ").Length - 1);
    }

    [Fact]
    public void A_card_without_an_href_is_not_a_link() =>
        Assert.DoesNotContain("<a ", UiCard.Heading("Jobs")[Span["body"]].ToHtml(), StringComparison.Ordinal);
}
