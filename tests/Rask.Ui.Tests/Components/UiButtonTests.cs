using System.Text.RegularExpressions;
using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's button: what each prop writes, its two tags, its parts, and Rask's own behaviour on top.
/// </summary>
/// <remarks>
///     How it LOOKS is held to fluxui.dev by <c>scripts/flux/parity.mjs button</c>; these hold the markup
///     contract. Deriving from <c>RaskMarkup</c> is what makes <c>Ui.Button</c> here the chain's entry.
/// </remarks>
public partial class UiButtonTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_plain_button_is_the_outline_at_the_base_size_and_shows_its_children()
    {
        var html = Ui.Button["Save"].ToHtml();

        Assert.StartsWith("<button class=\"relative inline-flex ", html, StringComparison.Ordinal);
        Assert.Contains(" h-10 px-4 text-sm rounded-lg gap-2 ", html, StringComparison.Ordinal);
        Assert.Contains(" bg-white ", html, StringComparison.Ordinal);
        Assert.EndsWith(" data-ui-button type=\"button\">Save</button>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.ButtonVariant.Outline, "border-b-zinc-300/80")]
    [InlineData(Ui.ButtonVariant.Primary, "bg-fx-accent")]
    [InlineData(Ui.ButtonVariant.Filled, "bg-zinc-800/5")]
    [InlineData(Ui.ButtonVariant.Danger, "bg-red-500")]
    [InlineData(Ui.ButtonVariant.Ghost, "hover:bg-zinc-800/5")]
    [InlineData(Ui.ButtonVariant.Subtle, "text-zinc-500")]
    public void Every_variant_draws_its_own_surface(Ui.ButtonVariant variant, string expected) =>
        Assert.Contains(expected, Ui.Button.Variant(variant)["Save"].ToHtml(), StringComparison.Ordinal);

    [Theory]
    [InlineData(Ui.ButtonSize.Base, "h-10 px-4 text-sm rounded-lg gap-2")]
    [InlineData(Ui.ButtonSize.Sm, "h-8 px-3 text-sm rounded-md gap-2")]
    [InlineData(Ui.ButtonSize.Xs, "h-6 px-2 text-xs rounded-md gap-1")]
    public void Every_size_sets_its_height_padding_and_type(Ui.ButtonSize size, string expected) =>
        Assert.Contains(expected, Ui.Button.Size(size)["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_variant_a_size_and_a_colour_are_steps_that_compose()
    {
        var html = Ui.Button.Filled.Sm.Blue["Save"].ToHtml();

        Assert.Contains("h-8 px-3", html, StringComparison.Ordinal);
        Assert.Contains("bg-(--ui-hue-400)/20", html, StringComparison.Ordinal);
        Assert.Contains("[--ui-hue-400:var(--color-blue-400)]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_smallest_outline_casts_no_shadow()
    {
        var small = Ui.Button.Xs["Save"].ToHtml();
        var regular = Ui.Button["Save"].ToHtml();

        Assert.Contains("shadow-none", small, StringComparison.Ordinal);
        Assert.Contains("shadow-xs", regular, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hue_on_the_outline_keeps_the_white_surface_and_mixes_into_text_and_border()
    {
        var html = Ui.Button.Color(Ui.Color.Green)["Approve"].ToHtml();

        Assert.Contains(" bg-white ", html, StringComparison.Ordinal);
        Assert.Contains("text-(color:--ui-hue-700)", html, StringComparison.Ordinal);
        Assert.Contains("border-[color:color-mix(in_oklab,var(--ui-hue-500)_18%,var(--color-zinc-200))]", html, StringComparison.Ordinal);
        Assert.Contains("[--ui-hue-500:var(--color-green-500)]", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Color.Blue, "var(--color-blue-400)")]
    [InlineData(Ui.Color.Indigo, "var(--color-indigo-300)")]
    [InlineData(Ui.Color.Purple, "var(--color-purple-300)")]
    public void The_shade_that_reads_on_a_dark_surface_is_400_and_300_for_indigo_and_purple(Ui.Color hue, string shade) =>
        Assert.Contains("[--ui-hue-accent:" + shade + "]", Ui.Button.Color(hue)["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_primary_of_a_colour_repoints_the_accent_for_that_one_button()
    {
        var blue = Ui.Button.Primary.Blue["Save"].ToHtml();
        var amber = Ui.Button.Primary.Amber["Save"].ToHtml();

        Assert.Contains("[--color-fx-accent:var(--color-blue-500)] [--color-fx-accent-foreground:var(--color-white)]", blue, StringComparison.Ordinal);
        Assert.Contains("[--color-fx-accent:var(--color-amber-400)] [--color-fx-accent-foreground:var(--color-amber-950)]", amber, StringComparison.Ordinal);
        Assert.DoesNotContain("--ui-hue-", blue, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gray_changes_a_primary_button_and_no_other()
    {
        var primary = Ui.Button.Primary.Slate["Save"].ToHtml();
        var outline = Ui.Button.Slate["Save"].ToHtml();

        Assert.Contains("[--color-fx-accent:var(--color-slate-800)]", primary, StringComparison.Ordinal);
        Assert.Equal(Ui.Button["Save"].ToHtml(), outline);
    }

    [Fact]
    public void Danger_is_red_whatever_colour_it_is_given() =>
        Assert.Equal(Ui.Button.Danger["Delete"].ToHtml(), Ui.Button.Danger.Blue["Delete"].ToHtml());

    [Fact]
    public void An_icon_sits_before_the_label_which_is_wrapped_and_the_button_pads_less_on_its_side()
    {
        var html = Ui.Button.Icon(Ui.IconName.ArrowDownTray)["Export"].ToHtml();

        Assert.Contains("h-10 ps-3 pe-4", html, StringComparison.Ordinal);
        Assert.Matches("<svg class=\"shrink-0 [^\"]*size-4\"[^>]*viewBox=\"0 0 16 16\"", html);
        Assert.EndsWith("</svg><span>Export</span></button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_icon_sits_after_the_label()
    {
        var html = Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Open"].ToHtml();

        Assert.Contains("h-10 ps-4 pe-3", html, StringComparison.Ordinal);
        Assert.Contains("<span>Open</span><svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_on_each_side_pads_both_sides_alike() =>
        Assert.Contains(
            "h-10 px-3",
            Ui.Button.Icon(Ui.IconName.Funnel).IconTrailing(Ui.IconName.ChevronDown)["Filter"].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void An_icon_with_no_children_is_a_square_holding_the_20px_drawing()
    {
        var html = Ui.Button.Icon(Ui.IconName.XMark).ToHtml();

        Assert.Contains("h-10 w-10", html, StringComparison.Ordinal);
        Assert.Matches("<svg class=\"shrink-0 [^\"]*size-5\"[^>]*viewBox=\"0 0 20 20\"", html);
        Assert.DoesNotContain("<span", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_smallest_square_holds_the_16px_drawing() =>
        Assert.Matches(
            "<svg class=\"shrink-0 [^\"]*size-4\"[^>]*viewBox=\"0 0 16 16\"",
            Ui.Button.Xs.Icon(Ui.IconName.XMark).ToHtml());

    [Fact]
    public void An_icon_variant_picks_the_drawing_and_the_button_still_sizes_it() =>
        Assert.Matches(
            "<svg class=\"shrink-0 [^\"]*size-5\"[^>]*viewBox=\"0 0 24 24\"",
            Ui.Button.Icon(Ui.IconName.Cog6Tooth).IconVariant(Ui.IconVariant.Outline).ToHtml());

    [Fact]
    public void Square_makes_a_labelled_button_square_and_false_keeps_an_icon_only_one_padded()
    {
        var square = Ui.Button.Square()["..."].ToHtml();
        var padded = Ui.Button.Icon(Ui.IconName.XMark).Square(false).ToHtml();

        Assert.Contains("h-10 w-10", square, StringComparison.Ordinal);
        Assert.Contains("h-10 ps-3 pe-4", padded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Align.Start, "justify-start")]
    [InlineData(Ui.Align.Center, "justify-center")]
    [InlineData(Ui.Align.End, "justify-end")]
    public void Align_places_the_content_in_a_wide_button(Ui.Align align, string expected) =>
        Assert.Contains(expected, Ui.Button.Align(align).Class("w-full")["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Inset_pulls_only_the_named_sides_out_by_what_the_size_pads()
    {
        var html = Ui.Button.Ghost.Sm.Inset(Ui.Inset.Top | Ui.Inset.Bottom)["Edit"].ToHtml();
        var all = Ui.Button.Ghost.Inset(Ui.Inset.All)["Edit"].ToHtml();

        Assert.Contains("-mt-1.5 -mb-1.5", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-ms-", html, StringComparison.Ordinal);
        Assert.Contains("-mt-2.5 -mb-2.5 -ms-2.5 -me-2.5", all, StringComparison.Ordinal);
    }

    [Fact]
    public void A_button_with_nothing_to_wait_on_carries_no_spinner() =>
        Assert.DoesNotContain("data-ui-loading-indicator", Ui.Button["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_button_with_a_click_handler_carries_the_spinner_the_runtime_will_show()
    {
        var html = Ui.Button.OnClick(() => { })["Save"].ToHtml();

        Assert.Contains("<div class=\"absolute inset-0 flex items-center justify-center opacity-0 ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-loading-indicator><svg class=\"shrink-0 [:where(&amp;)]:size-4 animate-spin\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"transition-opacity [[data-loading]&gt;&amp;]:opacity-0\">Save</span>", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(" data-loading[ >]", html);
    }

    [Fact]
    public void A_submit_button_carries_the_spinner_too() =>
        Assert.Contains(
            "data-ui-loading-indicator",
            Ui.Button.Type(Ui.ButtonType.Submit)["Save"].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void Loading_true_marks_it_from_the_render_with_the_one_attribute_and_no_aria()
    {
        var html = Ui.Button.Id("save").Data("testid", "save").AriaLabel("Save").Loading(true)["Save"].ToHtml();

        Assert.StartsWith("<button id=\"save\" class=\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "\" data-ui-button data-loading data-testid=\"save\" aria-label=\"Save\" type=\"button\"><div ",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("aria-busy", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_false_opts_out_of_the_runtimes_mark_and_keeps_the_markup_loading_true_has()
    {
        var off = Ui.Button.Loading(false)["+"].ToHtml();
        var on = Ui.Button.Loading(true)["+"].ToHtml();

        Assert.Contains("data-rask-loading=\"off\"", off, StringComparison.Ordinal);
        Assert.DoesNotMatch(" data-loading[ >]", off);
        Assert.Equal(Regex.Replace(on, "<button[^>]*>", ""), Regex.Replace(off, "<button[^>]*>", ""));
    }

    [Fact]
    public void A_link_waits_on_nothing()
    {
        var html = Ui.Button.Href("/orders").Loading(true)["Orders"].ToHtml();

        Assert.DoesNotMatch(" data-loading[ >]", html);
        Assert.DoesNotContain("aria-busy", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-loading-indicator", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_twice_gives_the_same_markup_and_leaves_the_call_sites_data_untouched()
    {
        var own = new Dictionary<string, string?> { ["testid"] = "save" };
        var button = Ui.Button.Data(own).Icon(Ui.IconName.Check).Loading(true)["Save"];

        var first = button.ToHtml();
        var second = button.ToHtml();

        Assert.Equal(first, second);
        Assert.Same(own, ((UiButton)button).Data);
        Assert.Single(own);
    }

    [Fact]
    public void A_tooltip_is_a_hidden_hint_inside_the_button_and_names_one_that_shows_only_an_icon()
    {
        var html = Ui.Button.Icon(Ui.IconName.Cog6Tooth).Tooltip("Settings").ToHtml();

        Assert.Contains(" aria-label=\"Settings\"", html, StringComparison.Ordinal);
        Assert.Matches("<span class=\"[^\"]*bottom-full[^\"]*\" data-ui-tooltip-content role=\"tooltip\" aria-hidden=\"true\">Settings</span></button>$", html);
    }

    [Fact]
    public void A_tooltip_leaves_a_labelled_button_named_by_its_label() =>
        Assert.DoesNotContain("aria-label", Ui.Button.Tooltip("Saves the draft")["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_label_the_call_site_wrote_wins_over_the_tooltip_and_is_written_once()
    {
        var html = Ui.Button.Icon(Ui.IconName.XMark).Tooltip("Close").AriaLabel("Close the dialog").ToHtml();

        Assert.Contains("aria-label=\"Close the dialog\"", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "aria-label="));
    }

    [Theory]
    [InlineData(Ui.Position.Top, "bottom-full")]
    [InlineData(Ui.Position.Bottom, "top-full")]
    [InlineData(Ui.Position.Left, "right-full")]
    [InlineData(Ui.Position.Right, "left-full")]
    public void The_tooltip_opens_on_the_side_it_is_given(Ui.Position position, string expected) =>
        Assert.Contains(
            expected,
            Ui.Button.Tooltip("Settings").TooltipPosition(position)["Open"].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void A_keyboard_shortcut_is_shown_at_the_end_of_the_tooltip_or_as_one_of_its_own()
    {
        var both = Ui.Button.Tooltip("Save").TooltipKbd("Esc")["Save"].ToHtml();
        var alone = Ui.Button.Kbd("K")["Search"].ToHtml();

        Assert.Contains("role=\"tooltip\" aria-hidden=\"true\">Save<span class=\"text-zinc-300 not-first:ps-1\">Esc</span></span>", both, StringComparison.Ordinal);
        Assert.Contains("role=\"tooltip\" aria-hidden=\"true\"><span class=\"text-zinc-300 not-first:ps-1\">K</span></span>", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_button_is_disabled_by_attribute() =>
        Assert.Contains(" disabled>", Ui.Button.Disabled(true)["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Call_site_classes_are_added_after_the_kits_own() =>
        Assert.Contains(" mt-2\" data-ui-button", Ui.Button.Class("mt-2")["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Every_element_step_reaches_the_button_in_the_documented_order()
    {
        var html = Ui.Button
            .Id("save")
            .Data("testid", "save")
            .Role("switch")
            .TabIndex(0)
            .AriaLabel("Save the draft")
            .Type(Ui.ButtonType.Reset)["Save"]
            .ToHtml();

        Assert.Matches(
            "^<button id=\"save\" class=\"[^\"]*\" data-ui-button data-testid=\"save\" role=\"switch\" tabindex=\"0\" "
            + "aria-label=\"Save the draft\" type=\"reset\">Save</button>$",
            html);
    }

    [Fact]
    public void Command_and_command_for_make_it_an_invoker()
    {
        var html = Ui.Button.Command("show-modal").CommandFor("confirm")["Delete"].ToHtml();

        Assert.Contains("command=\"show-modal\"", html, StringComparison.Ordinal);
        Assert.Contains("commandfor=\"confirm\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_an_href_it_is_an_anchor_that_looks_the_same()
    {
        var link = Ui.Button.Href("/docs")["Read the guide"].ToHtml();
        var button = Ui.Button["Read the guide"].ToHtml();

        Assert.StartsWith("<a class=\"", link, StringComparison.Ordinal);
        Assert.EndsWith(" data-ui-button href=\"/docs\">Read the guide</a>", link, StringComparison.Ordinal);
        Assert.Equal(Regex.Match(button, "class=\"[^\"]*\"").Value, Regex.Match(link, "class=\"[^\"]*\"").Value);
    }

    [Fact]
    public void An_anchor_href_is_sanitised_as_cores_own_anchor_sanitises_it()
    {
        var html = Ui.Button.Href("javascript:alert(1)")["Go"].ToHtml();

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            Regex.Match(A.Href("javascript:alert(1)").ToHtml(), "href=\"[^\"]*\"").Value,
            Regex.Match(html, "href=\"[^\"]*\"").Value);
    }

    [Fact]
    public void An_anchor_is_never_disabled_never_typed_and_never_an_invoker()
    {
        var html = Ui.Button.Href("/x").Disabled(true).Type(Ui.ButtonType.Submit).Command("close")["Go"].ToHtml();

        Assert.DoesNotMatch(" disabled[ >]", html);
        Assert.DoesNotContain("type=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("command", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_tab_carries_the_rel_that_makes_it_safe()
    {
        var html = Ui.Button.Href("https://example.test").NewTab(true)["Docs"].ToHtml();

        Assert.Contains("target=\"_blank\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void New_tab_without_an_href_changes_nothing() =>
        Assert.DoesNotContain("target", Ui.Button.NewTab(true)["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_generated_route_navigates_inside_the_app() =>
        Assert.EndsWith(
            " href=\"/orders\" data-rask-nav>Orders</a>",
            Ui.Button.Href(new RouteUrl("/orders", null, typeof(UiButtonTests)))["Orders"].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void A_string_stays_an_ordinary_link_even_to_an_in_app_path() =>
        Assert.DoesNotContain("data-rask-nav", Ui.Button.Href("/orders")["Orders"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_null_string_destination_leaves_it_a_button() =>
        Assert.Equal(Ui.Button["Save"].ToHtml(), Ui.Button.Href((string)null!)["Save"].ToHtml());

    [Fact]
    public void A_generated_route_in_a_new_tab_is_not_intercepted()
    {
        var html = Ui.Button.Href(new RouteUrl("/orders", null, typeof(UiButtonTests))).NewTab(true)["Orders"].ToHtml();

        Assert.Contains("target=\"_blank\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-nav", html, StringComparison.Ordinal);
    }

    [Fact]
    public void As_a_div_it_keeps_the_look_and_drops_what_only_a_button_has()
    {
        var html = Ui.Button.As(Ui.ButtonAs.Div).Disabled(true).OnClick(() => { })["Drop a file"].ToHtml();

        Assert.StartsWith("<div class=\"relative inline-flex ", html, StringComparison.Ordinal);
        Assert.EndsWith(" data-ui-button>Drop a file</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void As_an_anchor_without_an_href_it_is_an_anchor_with_none() =>
        Assert.EndsWith(" data-ui-button>Top</a>", Ui.Button.As(Ui.ButtonAs.A)["Top"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_group_is_a_flex_row_its_buttons_fuse_inside()
    {
        var html = Ui.ButtonGroup[Ui.Button["Oldest"], Ui.Button["Newest"]].ToHtml();

        Assert.StartsWith("<div class=\"flex\" data-ui-button-group><button ", html, StringComparison.Ordinal);
        Assert.Contains("in-data-ui-button-group:not-first:border-s-0", html, StringComparison.Ordinal);
        Assert.Contains("in-data-ui-button-group:first:rounded-s-lg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ghost_button_has_no_surface_to_fuse_so_a_group_leaves_it_alone() =>
        Assert.DoesNotContain("in-data-ui-button-group", Ui.Button.Ghost["More"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_group_keeps_the_call_sites_classes_and_data() =>
        Assert.StartsWith(
            "<div class=\"flex w-full\" data-ui-button-group data-testid=\"sort\">",
            Ui.ButtonGroup.Class("w-full").Data("testid", "sort")[Ui.Button["Top"]].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void Every_class_a_button_can_write_is_in_the_shipped_stylesheet()
    {
        var buttons = new List<string>();
        foreach (var variant in Enum.GetValues<Ui.ButtonVariant>())
        {
            foreach (var size in Enum.GetValues<Ui.ButtonSize>())
            {
                buttons.Add(Ui.Button.Variant(variant).Size(size).Inset(Ui.Inset.All).Loading(true).Tooltip("t").Kbd("k")["x"].ToHtml());
                buttons.Add(Ui.Button.Variant(variant).Size(size).Icon(Ui.IconName.Check).IconTrailing(Ui.IconName.Check)["x"].ToHtml());
                buttons.Add(Ui.Button.Variant(variant).Size(size).Icon(Ui.IconName.Check).ToHtml());
                buttons.Add(Ui.Button.Variant(variant).Size(size).IconTrailing(Ui.IconName.Check)["x"].ToHtml());
            }

            buttons.AddRange(Enum.GetValues<Ui.Color>().Select(color => Ui.Button.Variant(variant).Color(color)["x"].ToHtml()));
        }

        var written = buttons
            .SelectMany(html => Regex.Matches(html, "class=\"([^\"]*)\"").Select(match => match.Groups[1].Value))
            .SelectMany(classes => System.Net.WebUtility.HtmlDecode(classes).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.All(written, name => Assert.Contains("." + CssEscaped(name), UiStylesheet.Css, StringComparison.Ordinal));
    }

    // A class name as a stylesheet spells it: everything but letters, digits, `-` and `_` behind a backslash.
    private static string CssEscaped(string name) =>
        Regex.Replace(name, "[^A-Za-z0-9_-]", match => "\\" + match.Value);
}
