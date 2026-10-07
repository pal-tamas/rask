using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's callout: what each prop writes, where the slots sit, and that it says nothing to a screen
///     reader the call site did not ask it to.
/// </summary>
public partial class UiCalloutTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_callout_is_marked_and_announces_nothing_unless_told_to()
    {
        var callout = Ui.Callout.Heading("Saved");

        var html = callout.ToHtml();

        Assert.StartsWith("<div class=\"@container flex p-2 border rounded-xl bg-white border-zinc-200 ", html, StringComparison.Ordinal);
        Assert.Contains(" data-ui-callout>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-live", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_sites_id_class_and_role_land_on_the_callout_in_the_documented_order()
    {
        var callout = Ui.Callout.Danger.Id("failed").Class("mt-3").Role("alert").Heading("Payment failed");

        var html = callout.ToHtml();

        Assert.Matches("^<div id=\"failed\" class=\"@container [^\"]* mt-3\" data-ui-callout role=\"alert\">", html);
    }

    [Fact]
    public void The_heading_and_text_shorthands_are_the_parts_themselves_ahead_of_the_children()
    {
        var shorthand = Ui.Callout.Heading("Saved").Text("Nothing else to do.")[Span["after"]];
        var spelled = Ui.Callout[Ui.CalloutHeading["Saved"], Ui.CalloutText["Nothing else to do."], Span["after"]];

        var html = shorthand.ToHtml();

        Assert.Equal(spelled.ToHtml(), html);
        Assert.Contains(
            "<div class=\"flex items-center gap-2 text-sm font-medium\" data-slot=\"heading\">Saved</div>"
            + "<div class=\"text-sm\" data-slot=\"text\">Nothing else to do.</div><span>after</span></div>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_sits_in_its_own_column_and_is_the_20px_drawing_unless_another_is_asked_for()
    {
        var callout = Ui.Callout.Icon(Ui.IconName.Clock).Heading("Soon");

        var html = callout.ToHtml();

        Assert.Contains("data-ui-callout><div class=\"flex items-baseline py-2 ps-2\"><svg class=\"shrink-0 [:where(&amp;)]:size-5\"", html, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 24 24\"", Ui.Callout.Icon(Ui.IconName.Clock).Outline.ToHtml(), StringComparison.Ordinal);
        Assert.DoesNotContain("items-baseline", Ui.Callout.Heading("Soon").ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_icon_takes_the_place_of_the_named_one()
    {
        var callout = Ui.Callout.Icon(Ui.IconName.Clock).CustomIcon(Span.Id("mine")["m"]).Heading("Soon");

        var html = callout.ToHtml();

        Assert.Contains("<div class=\"flex items-baseline py-2 ps-2\"><span id=\"mine\">m</span></div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_on_the_heading_is_drawn_inside_the_headings_own_line()
    {
        var heading = Ui.CalloutHeading.Icon(Ui.IconName.Newspaper)["Policy update"];

        var html = Ui.Callout[heading].ToHtml();

        Assert.Contains("data-slot=\"heading\"><svg class=\"shrink-0 [:where(&amp;)]:size-5\"", html, StringComparison.Ordinal);
        Assert.EndsWith("</svg>Policy update</div></div></div></div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("items-baseline", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.CalloutVariant.Secondary, "bg-zinc-50 border-zinc-200 ")]
    [InlineData(Ui.CalloutVariant.Success, "bg-green-50 border-green-300 ")]
    [InlineData(Ui.CalloutVariant.Warning, "bg-yellow-50 border-yellow-400 ")]
    [InlineData(Ui.CalloutVariant.Danger, "bg-red-50 border-red-200 ")]
    public void Each_variant_is_drawn_in_its_own_hue(Ui.CalloutVariant variant, string surface)
    {
        var callout = Ui.Callout.Variant(variant).Heading("x");

        var html = callout.ToHtml();

        Assert.Contains(surface, html, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-white", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_wins_over_the_variant_and_every_grey_is_zinc()
    {
        var purple = Ui.Callout.Danger.Color(Ui.Color.Purple).Heading("x");

        var html = purple.ToHtml();

        Assert.Contains("bg-purple-50 border-purple-300 ", html, StringComparison.Ordinal);
        Assert.Contains("[&amp;_[data-slot=heading]]:text-purple-800 ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("red", html, StringComparison.Ordinal);
        Assert.All(
            (Ui.Color[])[Ui.Color.Slate, Ui.Color.Gray, Ui.Color.Zinc, Ui.Color.Neutral, Ui.Color.Stone],
            grey => Assert.Equal(Ui.Callout.Secondary.Heading("x").ToHtml(), Ui.Callout.Color(grey).Heading("x").ToHtml()));
    }

    [Fact]
    public void Every_hue_has_a_palette_of_its_own()
    {
        var hues = Enum.GetValues<Ui.Color>().Where(color => color <= Ui.Color.Rose).ToList();

        var surfaces = hues.Select(color => Ui.Callout.Color(color).Heading("x").ToHtml()).ToList();

        Assert.Equal(17, surfaces.Distinct(StringComparer.Ordinal).Count());
        Assert.All(hues.Zip(surfaces), pair =>
            Assert.Contains($"bg-{pair.First.ToString().ToLowerInvariant()}-50 ", pair.Second, StringComparison.Ordinal));
    }

    [Fact]
    public void Actions_go_under_the_content_and_controls_in_a_column_of_their_own()
    {
        var callout = Ui.Callout.Heading("x").Actions([Span["renew"], Span["plans"]]).Controls(Span["close"]);

        var html = callout.ToHtml();

        Assert.EndsWith(
            "<div class=\"flex items-center gap-2 self-start py-2\" data-slot=\"actions\"><span>renew</span><span>plans</span></div></div>"
            + "<div class=\"ps-2\"><span>close</span></div></div>",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@md:flex ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inline_callout_puts_its_actions_beside_the_content_once_it_is_wide_enough()
    {
        var callout = Ui.Callout.Inline().Heading("x").Actions(Span["track"]).Controls(Span["close"]);

        var html = callout.ToHtml();

        Assert.Contains("<div class=\"flex-1 ps-2 @md:flex\">", html, StringComparison.Ordinal);
        Assert.Contains("self-start py-2 @md:flex-row-reverse @md:justify-end @md:-m-0.5 @md:py-0 @md:ps-4 ", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"ps-2 -m-0.5\"><span>close</span></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_callout_with_no_actions_or_controls_writes_neither_slot()
    {
        var callout = Ui.Callout.Inline().Heading("x");

        var html = callout.ToHtml();

        Assert.DoesNotContain("data-slot=\"actions\"", html, StringComparison.Ordinal);
        Assert.EndsWith("data-slot=\"heading\">x</div></div></div></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_one_div_that_takes_every_element_step()
    {
        var text = Ui.CalloutText.Id("why").Class("mt-1").Data("testid", "why")["Because."];

        var html = text.ToHtml();

        Assert.Equal("<div id=\"why\" class=\"text-sm mt-1\" data-slot=\"text\" data-testid=\"why\">Because.</div>", html);
    }

    [Fact]
    public void A_link_to_a_url_is_a_plain_anchor_underlined_in_the_texts_own_colour()
    {
        var link = Ui.CalloutLink.Href("https://example.com/terms")["Learn more"];

        var html = link.ToHtml();

        Assert.Equal(
            "<a class=\"font-medium underline underline-offset-[6px] decoration-zinc-800/20 dark:decoration-white/20 hover:decoration-current\""
            + " href=\"https://example.com/terms\">Learn more</a>",
            html);
    }

    [Fact]
    public void An_external_link_opens_a_new_tab_that_cannot_reach_back()
    {
        var link = Ui.CalloutLink.Href("https://example.com").External()["Docs"];

        var html = link.ToHtml();

        Assert.EndsWith(" href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">Docs</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_to_a_generated_route_navigates_inside_the_app_and_never_opens_a_tab()
    {
        var route = new RouteUrl("/billing", PageType: typeof(UiCalloutTests));

        var html = Ui.CalloutLink.Href(route).External()["Billing"].ToHtml();

        Assert.EndsWith(" href=\"/billing\" data-rask-nav>Billing</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("target=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_with_nowhere_to_go_writes_no_href()
    {
        var link = Ui.CalloutLink["Later"];

        var html = link.ToHtml();

        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
    }
}
