namespace Rask.UiTests.Components;

/// <summary>
///     The megamenu, which the browser opens.
/// </summary>
public partial class UiMegamenuTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Each_panel_is_a_native_popover_named_by_its_trigger()
    {
        // This is the whole mechanism: no class, no state, no script. The browser supplies the top
        // layer, Escape and light-dismiss, and it works before any runtime has booted.
        var html = Menu();

        Assert.Contains("popovertarget=\"products\"", html);
        Assert.Contains("popover=\"auto\"", html);
        Assert.Contains("id=\"products\"", html);
    }

    [Fact]
    public void The_triggers_are_buttons_and_the_panels_are_their_siblings()
    {
        // daisyUI anchors the panels with :nth-of-type, which counts by TAG — so the triggers must be
        // siblings of one another and the panels siblings of one another. A wrapper around either
        // breaks the numbering and the panels open under the wrong trigger.
        var html = Menu();

        var firstTrigger = html.IndexOf("popovertarget=\"products\"", StringComparison.Ordinal);
        var firstPanel = html.IndexOf("id=\"products\"", StringComparison.Ordinal);
        var secondTrigger = html.IndexOf("popovertarget=\"company\"", StringComparison.Ordinal);

        Assert.True(firstTrigger < firstPanel);
        Assert.True(firstPanel < secondTrigger);
    }

    [Fact]
    public void The_highlight_is_the_last_child()
    {
        // It is a div, and daisyUI numbers the panels as [popover]:nth-of-type(n) — which counts divs.
        // Placed among them it would shift every panel's number.
        var html = Menu();

        Assert.EndsWith("<div class=\"megamenu-active\"></div></nav>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Size.Xs, "megamenu-xs")]
    [InlineData(Ui.Size.Lg, "megamenu-lg")]
    public void Every_size_writes_its_own_class(Ui.Size size, string expected) =>
        Assert.Contains(expected, Ui.Megamenu.Size(size)[Panel()].ToHtml());

    [Fact]
    public void Full_wide_and_vertical_are_each_opt_in()
    {
        Assert.Contains("megamenu-full", Ui.Megamenu.Full(true)[Panel()].ToHtml());
        Assert.Contains("megamenu-wide", Ui.Megamenu.Wide(true)[Panel()].ToHtml());
        Assert.Contains("megamenu-vertical", Ui.Megamenu.Vertical(true)[Panel()].ToHtml());
        Assert.DoesNotContain("megamenu-full", Menu());
    }

    [Fact]
    public void The_panel_writes_no_class_of_its_own()
    {
        // daisyUI styles it through `.megamenu [popover]`. A name invented here would sit in the markup
        // looking as though it styled something and style nothing.
        Assert.DoesNotContain("megamenu-panel", Menu());
    }

    private UiMegamenuPanel Panel() =>
        Ui.MegamenuPanel.Trigger("Products").Id("products");

    private string Menu() =>
        Ui.Megamenu[
            Ui.MegamenuPanel.Key("p").Trigger("Products").Id("products")[P["Everything we sell."]],
            Ui.MegamenuPanel.Key("c").Trigger("Company").Id("company")[P["Who we are."]]
        ].ToHtml();
}
