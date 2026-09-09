namespace Rask.Ui.Tests.Components;

/// <summary>
///     The panel, and the composition of its class attribute.
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
}
