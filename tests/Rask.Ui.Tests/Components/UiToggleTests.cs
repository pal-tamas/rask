using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's toggle: a button that says it is a switch, what each prop writes on it, and whose state it is.
/// </summary>
public partial class UiToggleTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_toggle_is_a_button_that_says_it_is_a_switch_and_starts_off()
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt);

        var html = toggle.ToHtml();

        Assert.StartsWith("<button class=\"group relative [:where(&amp;)]:inline-flex ", html, StringComparison.Ordinal);
        Assert.Contains(" data-ui-control data-ui-toggle data-rask-switch role=\"switch\" aria-checked=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-toggle data-checked", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Checked_starts_it_on_with_both_drawings_written_and_the_stylesheet_choosing_one()
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt).Checked();

        var html = toggle.ToHtml();

        Assert.Contains("data-ui-toggle data-checked", html, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"true\"", html, StringComparison.Ordinal);
        Assert.Matches("hidden group-data-checked:block[^>]*fill=\"currentColor\".*group-data-checked:hidden[^>]*stroke=\"currentColor\"", html);
    }

    [Fact]
    public void A_toggle_nobody_listens_to_carries_no_handler_and_is_the_runtimes_to_turn()
    {
        var settings = new Settings();
        var own = Ui.Toggle.Icon(Ui.IconName.Bolt).Checked();
        var heard = Ui.Toggle.Icon(Ui.IconName.Bolt).OnChange(_ => { });
        var bound = Ui.Toggle.Bind(() => settings.FastMode);

        var html = Page.Render(() => Div[own, heard, bound]).Html;

        // A press that only changes what it draws is the browser's: no handler, so not one frame is sent.
        Assert.Equal(2, html.Split("data-rask-on-click=").Length - 1);
        Assert.Equal(1, html.Split(" data-rask-switch").Length - 1);
        Assert.Matches("<button (?![^>]*data-rask-on-click)[^>]*data-checked data-rask-switch", html);
    }

    [Fact]
    public async Task An_unbound_toggle_keeps_its_own_state_and_reports_each_turn()
    {
        var seen = new List<bool>();
        var page = Page.Render(() => Ui.Toggle.Id("fast").Icon(Ui.IconName.Bolt).OnChange(seen.Add));

        await page.On("#fast").Click();
        var on = page.Html;
        await page.On("#fast").Click();

        Assert.Contains("aria-checked=\"true\"", on, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"false\"", page.Html, StringComparison.Ordinal);
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public async Task A_page_that_keeps_the_state_has_the_last_word_whenever_it_says_something_new()
    {
        var muted = false;
        var page = Page.Render(() => Ui.Toggle.Id("sound").Checked(muted).OnChange(on => muted = on));

        await page.On("#sound").Click();
        var turned = page.Html;
        muted = false;
        page.Render();

        Assert.Contains("aria-checked=\"true\"", turned, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"false\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bound_toggle_shows_its_turn_at_once_and_writes_the_member_with_the_next_action()
    {
        var settings = new Settings();
        var page = Page.Render(() => Div[Ui.Toggle.Id("fast").Bind(() => settings.FastMode), Button.Id("save").OnClick(() => { })["Save"]]);

        await page.On("#fast").Click();
        var waiting = settings.FastMode;
        var shown = page.Html;
        await page.On("#save").Click();

        Assert.False(waiting);
        Assert.Contains("aria-checked=\"true\"", shown, StringComparison.Ordinal);
        Assert.True(settings.FastMode);
    }

    [Fact]
    public async Task A_live_toggle_writes_the_member_as_it_is_turned()
    {
        var settings = new Settings { FastMode = true };
        var page = Page.Render(() => Ui.Toggle.Id("fast").Bind(() => settings.FastMode).Live());

        await page.On("#fast").Click();

        Assert.False(settings.FastMode);
        Assert.Contains("aria-checked=\"false\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bound_toggle_the_page_listens_to_is_written_first_and_heard_in_the_same_turn()
    {
        var settings = new Settings();
        var saved = new List<bool>();
        var page = Page.Render(() => Ui.Toggle.Id("active").Bind(() => settings.FastMode).OnChange(_ => saved.Add(settings.FastMode)));

        await page.On("#active").Click();

        Assert.Equal([true], saved);
    }

    [Fact]
    public void A_tooltip_wraps_the_toggle_and_names_it()
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Fast mode");

        var html = toggle.ToHtml();

        Assert.StartsWith("<div data-ui-tooltip", html, StringComparison.Ordinal);
        Assert.Matches("<button [^>]*aria-labelledby=\"(ui-tooltip-\\d+)\"[^>]*>.*id=\"\\1\"[^>]*>Fast mode", html);
        Assert.DoesNotContain("aria-label=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_is_written_once_per_state_and_the_stateful_ones_take_its_place()
    {
        var same = Ui.Toggle.Label("Fast mode").ToHtml();
        var stateful = Ui.Toggle.Label("Sound").OnLabel("Sound on").ToHtml();

        Assert.Contains("<span class=\"group-data-checked:hidden\">Fast mode</span><span class=\"hidden group-data-checked:block\">Fast mode</span>", same, StringComparison.Ordinal);
        Assert.Contains("<span class=\"group-data-checked:hidden\">Sound</span><span class=\"hidden group-data-checked:block\">Sound on</span>", stateful, StringComparison.Ordinal);
        Assert.Contains(" h-10 [:where(&amp;)]:px-4 ", same, StringComparison.Ordinal);
    }

    [Fact]
    public void Children_take_the_place_of_every_label()
    {
        var toggle = Ui.Toggle.Label("Sound").OnLabel("Sound on")[B["Loud"]];

        var html = toggle.ToHtml();

        Assert.Contains("<span><b>Loud</b></span></button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sound", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.ToggleSize.Base, "size-10", "rounded-lg", "size-5")]
    [InlineData(Ui.ToggleSize.Sm, "size-8", "rounded-md", "size-5")]
    [InlineData(Ui.ToggleSize.Xs, "size-6", "rounded-md", "size-4")]
    public void Each_size_is_a_square_of_its_own_with_its_own_corner_and_icon(Ui.ToggleSize size, string square, string corner, string icon)
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt).Size(size);

        var html = toggle.ToHtml();

        Assert.Contains("[:where(&amp;)]:" + square + " ", html, StringComparison.Ordinal);
        Assert.Contains("[:where(&amp;)]:" + corner + " ", html, StringComparison.Ordinal);
        Assert.Contains("group-data-checked:hidden " + icon + " ", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.ToggleVariant.Outline, "border border-zinc-200 border-b-zinc-300/80 bg-white shadow-xs")]
    [InlineData(Ui.ToggleVariant.Filled, "bg-zinc-800/5 hover:bg-zinc-800/10")]
    [InlineData(Ui.ToggleVariant.Ghost, "bg-transparent hover:bg-zinc-800/5")]
    [InlineData(Ui.ToggleVariant.Subtle, "text-zinc-500/85 hover:text-zinc-500")]
    public void Each_variant_is_drawn_on_its_own_surface(Ui.ToggleVariant variant, string expected)
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt).Variant(variant);

        var html = toggle.ToHtml();

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_is_the_icons_when_on_and_the_accent_stands_in_for_none()
    {
        var red = Ui.Toggle.Icon(Ui.IconName.Heart).Color(Ui.Color.Red).ToHtml();
        var plain = Ui.Toggle.Icon(Ui.IconName.Heart).ToHtml();

        Assert.Contains("group-data-checked:text-red-600 dark:group-data-checked:text-red-400", red, StringComparison.Ordinal);
        Assert.Contains("group-data-checked:text-fx-accent-content", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("fx-accent-content", red, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_stateful_icons_are_each_drawn_for_their_own_state()
    {
        var toggle = Ui.Toggle.OnIcon(Ui.IconName.SpeakerWave).OffIcon(Ui.IconName.SpeakerXMark);

        var html = toggle.ToHtml();

        Assert.Equal(3, html.Split("<svg").Length - 1);
        Assert.Matches("hidden group-data-checked:block[^>]*fill=\"currentColor\".*group-data-checked:hidden[^>]*stroke=\"currentColor\"", html);
    }

    [Fact]
    public void Disabled_is_the_buttons_own_attribute()
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt).Disabled();

        var html = toggle.ToHtml();

        Assert.Matches("<button [^>]* disabled[ >]", html);
    }

    [Fact]
    public void Icons_of_the_apps_own_take_the_place_of_the_kits_two()
    {
        var toggle = Ui.Toggle.Icon(Raw.Value("<svg id=\"own\"></svg>"));

        var html = toggle.ToHtml();

        Assert.Contains("</div><svg id=\"own\"></svg></button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_spinner_is_always_there_and_shown_only_while_the_runtime_marks_the_button_waiting()
    {
        var toggle = Ui.Toggle.Icon(Ui.IconName.Bolt);

        var html = toggle.ToHtml();

        Assert.Contains("opacity-0 group-data-loading:opacity-100!\" data-ui-loading-indicator aria-hidden=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-loading:pointer-events-none", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" data-loading=", html, StringComparison.Ordinal);
    }

    private sealed class Settings
    {
        public bool FastMode { get; set; }
    }
}
