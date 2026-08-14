namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsAccordion / BsAccordionItem. ToHtml() renders static markup (no live
// context), which is all these structural checks need.
public partial class BsAccordionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Accordion_WrapsItemsInAccordionDiv() =>
        Assert.Equal(
            "<div class=\"accordion\"><div class=\"accordion-item\">" +
            "<h2 class=\"accordion-header\">" +
            "<button class=\"accordion-button collapsed\" aria-expanded=\"false\" type=\"button\">Section</button>" +
            "</h2>" +
            "<div class=\"accordion-collapse collapse\"><div class=\"accordion-body\">Body</div></div>" +
            "</div></div>",
            BsAccordion[BsAccordionItem.Title("Section")["Body"]].ToHtml());

    [Fact]
    public void AccordionItem_Open_ShowsPanelAndExpandsButton() =>
        Assert.Equal(
            "<div class=\"accordion-item\">" +
            "<h2 class=\"accordion-header\">" +
            "<button class=\"accordion-button\" aria-expanded=\"true\" type=\"button\">Open</button>" +
            "</h2>" +
            "<div class=\"accordion-collapse collapse show\"><div class=\"accordion-body\">Panel</div></div>" +
            "</div>",
            BsAccordionItem.Title("Open").Open(true)["Panel"].ToHtml());

    [Fact]
    public void Accordion_Flush_AddsFlushModifier() =>
        Assert.Equal(
            "<div class=\"accordion accordion-flush\"></div>",
            BsAccordion.Flush(true).ToHtml());
}
