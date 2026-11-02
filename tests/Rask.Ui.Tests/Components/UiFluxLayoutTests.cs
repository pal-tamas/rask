namespace Rask.UiTests.Components;

/// <summary>
///     The layout pieces taken from Flux UI: the separator, the spacer, the sidebar and its toggle, the navigation
///     list, and the type scale.
/// </summary>
public partial class UiFluxLayoutTests : global::Rask.Core.RaskMarkup
{
    // ---- spacer ---------------------------------------------------------------------------------

    [Fact]
    public void A_spacer_is_an_empty_div_that_grows_with_no_aria_as_Fluxs_is() =>
        Assert.Equal("<div class=\"flex-1\" data-ui-spacer=\"\"></div>", Ui.Spacer.ToHtml());

    // ---- type -----------------------------------------------------------------------------------

    [Fact]
    public void A_card_heading_takes_a_heading_level()
    {
        Assert.Contains("<div", Ui.CardHeading["Total"].ToHtml());
        Assert.Contains("<h3", Ui.CardHeading.Level(3)["Total"].ToHtml());
    }
}
