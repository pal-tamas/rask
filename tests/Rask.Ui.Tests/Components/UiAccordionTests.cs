using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's accordion on the platform's own disclosure: what each part writes, and who owns the open state.
/// </summary>
public partial class UiAccordionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_item_is_a_details_whose_summary_is_the_heading_and_each_part_carries_its_marker()
    {
        var page = Page.Render(() => Ui.Accordion[
            Ui.AccordionItem[Ui.AccordionHeading["Shipping"], Ui.AccordionContent["Two days."]]
        ]);

        var item = page.Find("div[data-ui-accordion] > details[data-ui-accordion-item]");

        Assert.Equal("Shipping", page.TextOf("details > summary[data-ui-accordion-heading] > span"));
        Assert.Equal("Two days.", page.TextOf("details > div[data-ui-accordion-content] > div"));
        Assert.Null(item.Attribute("open"));
        Assert.Null(item.Attribute("name"));
    }

    [Fact]
    public void The_heading_shorthand_writes_the_same_markup_as_the_two_parts()
    {
        var parts = Ui.Accordion[Ui.AccordionItem[Ui.AccordionHeading["Shipping"], Ui.AccordionContent["Two days."]]].ToHtml();

        var shorthand = Ui.Accordion[Ui.AccordionItem.Heading("Shipping")["Two days."]].ToHtml();

        Assert.Equal(parts, shorthand);
    }

    [Fact]
    public void Nothing_in_an_accordion_is_drawn_by_daisyUI()
    {
        var html = Ui.Accordion.Exclusive().Transition().Reverse[
            Ui.AccordionItem.Heading("Shipping").Expanded()["Two days."],
            Ui.AccordionItem.Heading("Payment").Disabled()["Card."]
        ].ToHtml();

        var classes = ClassAttribute().Matches(html).SelectMany(m => m.Groups[1].Value.Split(' ')).ToList();

        Assert.DoesNotContain(classes, name => name.StartsWith("collapse", StringComparison.Ordinal) || name is "join" or "join-item");
        Assert.DoesNotContain(classes, name => name.Contains("base-", StringComparison.Ordinal) || name.Contains("-ui-", StringComparison.Ordinal));
    }

    [Fact]
    public void An_exclusive_accordion_names_its_items_as_one_group_so_the_browser_closes_the_others()
    {
        var page = Page.Render(() => Ui.Accordion.Exclusive()[
            Ui.AccordionItem.Heading("Shipping")["Two days."],
            Ui.AccordionItem.Heading("Payment")["Card."]
        ]);

        var names = page.Attrs("name");

        Assert.Equal(2, names.Count);
        Assert.StartsWith("ui-accordion-", names[0], StringComparison.Ordinal);
        Assert.Equal(names[0], names[1]);
    }

    [Fact]
    public void Two_exclusive_accordions_are_two_groups_and_each_keeps_its_name_across_renders()
    {
        var page = Page.Render(() => Div[
            Ui.Accordion.Key("a").Exclusive()[Ui.AccordionItem.Heading("Shipping")["Two days."]],
            Ui.Accordion.Key("b").Exclusive()[Ui.AccordionItem.Heading("Payment")["Card."]]
        ]);
        var first = page.Attrs("name");

        page.Render();

        Assert.NotEqual(first[0], first[1]);
        Assert.Equal(first, page.Attrs("name"));
    }

    [Fact]
    public void An_expanded_item_is_rendered_open_and_the_others_closed()
    {
        var page = Page.Render(() => Ui.Accordion[
            Ui.AccordionItem.Key("ship").Heading("Shipping")["Two days."],
            Ui.AccordionItem.Key("pay").Heading("Payment").Expanded()["Card."]
        ]);

        var items = page.FindAll("details");

        Assert.Null(items[0].Attribute("open"));
        Assert.NotNull(items[1].Attribute("open"));
    }

    [Fact]
    public void An_item_with_no_handler_sends_nothing_to_the_server_because_the_browser_opens_it()
    {
        var html = Ui.Accordion[Ui.AccordionItem.Heading("Shipping")["Two days."]].ToHtml();

        Assert.DoesNotContain("data-rask-on-", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_heading_leaves_the_tab_order_takes_no_pointer_and_says_so()
    {
        var page = Page.Render(() => Ui.Accordion[
            Ui.AccordionItem.Key("ship").Heading("Shipping")["Two days."],
            Ui.AccordionItem.Key("pay").Heading("Payment").Disabled()["Card."]
        ]);

        var headings = page.FindAll("summary");

        Assert.Null(headings[0].Attribute("tabindex"));
        Assert.Null(headings[0].Attribute("aria-disabled"));
        Assert.Equal("-1", headings[1].Attribute("tabindex"));
        Assert.Equal("true", headings[1].Attribute("aria-disabled"));
        Assert.Contains("pointer-events-none", headings[1].Attribute("class"), StringComparison.Ordinal);
        Assert.Contains("text-zinc-400", headings[1].Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_chevron_follows_the_heading_and_turns_from_down_to_up()
    {
        var page = Page.Render(() => Ui.Accordion[Ui.AccordionItem.Heading("Shipping")["Two days."]]);

        var icons = page.FindAll("summary > svg");

        Assert.False(page.Find("summary").HasClass("flex-row-reverse"));
        Assert.Equal(Drawing(Ui.IconName.ChevronUp), icons[0].Children[0].Attribute("d"));
        Assert.Contains("ms-6 hidden", icons[0].Attribute("class"), StringComparison.Ordinal);
        Assert.Equal(Drawing(Ui.IconName.ChevronDown), icons[1].Children[0].Attribute("d"));
        Assert.Contains("ms-6 block group-open/accordion-item:hidden", icons[1].Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reverse_puts_the_chevron_before_the_heading_and_turns_it_from_right_to_down()
    {
        var page = Page.Render(() => Ui.Accordion.Reverse[Ui.AccordionItem.Heading("Shipping")["Two days."]]);

        var icons = page.FindAll("summary > svg");

        Assert.True(page.Find("summary").HasClass("flex-row-reverse") && page.Find("summary").HasClass("justify-end"));
        Assert.Equal(Drawing(Ui.IconName.ChevronDown), icons[0].Children[0].Attribute("d"));
        Assert.Equal(Drawing(Ui.IconName.ChevronRight), icons[1].Children[0].Attribute("d"));
        Assert.All(icons, icon => Assert.Contains("me-2", icon.Attribute("class"), StringComparison.Ordinal));
    }

    [Fact]
    public void Transition_is_a_mark_on_the_accordion_that_the_stylesheet_animates_each_items_content_slot_by()
    {
        var plain = Page.Render(() => Ui.Accordion[Ui.AccordionItem.Heading("Shipping")["Two days."]]);
        var animated = Page.Render(() => Ui.Accordion.Transition()[Ui.AccordionItem.Heading("Shipping")["Two days."]]);

        var rule = UiStylesheet.Css;

        Assert.Null(plain.Find("[data-ui-accordion]").Attribute("data-transition"));
        Assert.NotNull(animated.Find("[data-ui-accordion]").Attribute("data-transition"));
        Assert.Contains("[data-ui-accordion][data-transition]>[data-ui-accordion-item]::details-content", rule, StringComparison.Ordinal);
        Assert.Contains("interpolate-size:allow-keywords", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_outside_an_accordion_still_opens_as_a_plain_default_item()
    {
        var alone = Ui.AccordionItem.Heading("Shipping")["Two days."].ToHtml();

        var inside = Ui.Accordion[Ui.AccordionItem.Heading("Shipping")["Two days."]].ToHtml();

        Assert.StartsWith("<details ", alone, StringComparison.Ordinal);
        Assert.Contains(alone, inside, StringComparison.Ordinal);
    }

    private static string? Drawing(Ui.IconName name) =>
        Page.Render(() => Ui.Icon.Name(name).Mini).Find("svg").Children[0].Attribute("d");

    [GeneratedRegex("class=\"([^\"]*)\"")]
    private static partial Regex ClassAttribute();
}
