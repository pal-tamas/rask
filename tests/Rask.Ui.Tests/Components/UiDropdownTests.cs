namespace Rask.Ui.Tests.Components;

/// <summary>
///     The dropdown's three open states, which are the whole reason it stopped being a
///     <c>&lt;details&gt;</c>.
/// </summary>
public partial class UiDropdownTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_leaves_the_open_state_to_the_browser()
    {
        // Neither class, so daisyUI's `:focus-within` rule is what decides. This is the uncontrolled
        // shape, and it still works with no runtime attached.
        var html = UiDropdown.Trigger("Actions").ToHtml();

        Assert.DoesNotContain("dropdown-open", html);
        Assert.DoesNotContain("dropdown-close", html);
    }

    [Fact]
    public void Open_writes_the_open_class() =>
        Assert.Contains("dropdown-open", UiDropdown.Trigger("Actions").Open(true).ToHtml());

    [Fact]
    public void Closed_writes_dropdown_close_rather_than_merely_omitting_dropdown_open()
    {
        // This is the load-bearing one. Omitting `dropdown-open` is not enough, because daisyUI also
        // opens on `:focus-within` — so a dropdown the page had just closed would re-open the moment
        // focus landed inside it, and the state in C# and the state on screen would disagree with
        // nothing reporting it. `dropdown-close` outranks the focus rule.
        var html = UiDropdown.Trigger("Actions").Open(false).ToHtml();

        Assert.Contains("dropdown-close", html);
        Assert.DoesNotContain("dropdown-open", html);
    }

    [Theory]
    [InlineData(UiPlacement.Start, "dropdown-start")]
    [InlineData(UiPlacement.Center, "dropdown-center")]
    [InlineData(UiPlacement.End, "dropdown-end")]
    [InlineData(UiPlacement.Top, "dropdown-top")]
    [InlineData(UiPlacement.Bottom, "dropdown-bottom")]
    [InlineData(UiPlacement.Left, "dropdown-left")]
    [InlineData(UiPlacement.Right, "dropdown-right")]
    public void Every_placement_writes_its_own_class(UiPlacement placement, string expected) =>
        Assert.Contains(expected, UiDropdown.Trigger("Actions").Placement(placement).ToHtml());

    [Fact]
    public void The_default_placement_writes_no_class_at_all() =>
        Assert.Equal(
            UiDropdown.Trigger("Actions").ToHtml(),
            UiDropdown.Trigger("Actions").Placement(UiPlacement.Default).ToHtml());

    [Fact]
    public void Opening_on_hover_is_opt_in() =>
        Assert.Contains("dropdown-hover", UiDropdown.Trigger("Actions").OpenOn(UiOpenOn.Hover).ToHtml());

    [Fact]
    public void Clicking_is_the_default_and_writes_no_class() =>
        Assert.DoesNotContain("dropdown-hover", UiDropdown.Trigger("Actions").OpenOn(UiOpenOn.Click).ToHtml());

    [Fact]
    public void The_trigger_announces_the_open_state()
    {
        Assert.Contains("aria-expanded=\"true\"", UiDropdown.Trigger("Actions").Open(true).ToHtml());
        Assert.Contains("aria-expanded=\"false\"", UiDropdown.Trigger("Actions").Open(false).ToHtml());
    }

    [Fact]
    public void The_trigger_says_it_opens_a_menu() =>
        Assert.Contains("aria-haspopup=\"menu\"", UiDropdown.Trigger("Actions").ToHtml());

    [Fact]
    public void An_uncontrolled_trigger_carries_a_tabindex_so_daisyUIs_rules_apply()
    {
        // `[tabindex]:first-child` is how daisyUI reaches the trigger, and while uncontrolled that is
        // what should happen — including the rule that stops the trigger taking clicks while the panel
        // is open, which is what lets clicking away close it instead of the trigger re-opening it.
        Assert.Contains("tabindex=\"0\"", UiDropdown.Trigger("Actions").ToHtml());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_controlled_trigger_does_not_because_the_rule_it_opts_into_would_trap_it(bool open)
    {
        // daisyUI sets `pointer-events: none` on `[tabindex]:first-child` while the dropdown is open.
        // For a controlled dropdown that is fatal: the only way to close is OnToggle, OnToggle only
        // fires on a click, and the click cannot land — so it opens once and stays open. A browser test
        // found this exactly that way, as a click Playwright reported the container was intercepting.
        Assert.DoesNotContain("tabindex", UiDropdown.Trigger("Actions").Open(open).ToHtml());
    }

    [Fact]
    public void The_panel_is_the_element_daisyUI_positions() =>
        Assert.Contains("dropdown-content", UiDropdown.Trigger("Actions").ToHtml());
}
