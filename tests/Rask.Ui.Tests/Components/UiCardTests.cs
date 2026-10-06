namespace Rask.UiTests.Components;

public sealed partial class UiCardTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_card_says_what_it_is_in_its_markers()
    {
        var card = Ui.Card.Inset.Soft.Lg[Span["body"]];

        var html = card.ToHtml();

        Assert.StartsWith("<div ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-body-variant=\"inset\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-variant=\"soft\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-size=\"lg\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_with_nothing_said_is_a_seamless_default_medium_one()
    {
        var card = Ui.Card[Span["body"]];

        var html = card.ToHtml();

        Assert.Contains("data-ui-card-body-variant=\"seamless\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-variant=\"default\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-size=\"md\"", html, StringComparison.Ordinal);
        Assert.Contains("rounded-xl", html, StringComparison.Ordinal);
        Assert.Contains("p-6", html, StringComparison.Ordinal);
        Assert.Contains("shadow-xs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seamless_card_pads_what_is_put_straight_inside_it_and_the_other_treatments_leave_that_to_the_parts()
    {
        var seamless = Ui.Card.Sm["x"].ToHtml();
        var inset = Ui.Card.Inset["x"].ToHtml();
        var divided = Ui.Card.Divided["x"].ToHtml();

        Assert.Contains(" p-4 ", seamless, StringComparison.Ordinal);
        Assert.Contains(" p-1 ", inset, StringComparison.Ordinal);
        Assert.DoesNotContain(" p-", divided, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.CardVariant.Default, "bg-white")]
    [InlineData(Ui.CardVariant.Muted, "bg-zinc-900/4")]
    [InlineData(Ui.CardVariant.Soft, "bg-zinc-900/2")]
    [InlineData(Ui.CardVariant.Outline, "border-zinc-900/10")]
    [InlineData(Ui.CardVariant.Filled, "border-transparent")]
    public void Each_variant_draws_its_own_surface(Ui.CardVariant variant, string surface)
    {
        var card = Ui.Card.Variant(variant)["x"];

        var html = card.ToHtml();

        Assert.Contains(surface, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_default_surface_is_raised_by_a_shadow()
    {
        var raised = Ui.Card["x"].ToHtml();
        var flat = Ui.Card.Outline["x"].ToHtml();

        Assert.Contains("shadow-xs", raised, StringComparison.Ordinal);
        Assert.DoesNotContain("shadow-xs", flat, StringComparison.Ordinal);
    }

    [Fact]
    public void The_edge_highlight_can_be_turned_off_and_a_filled_card_never_has_one()
    {
        var lit = Ui.Card["x"].ToHtml();
        var plain = Ui.Card.Highlight(false)["x"].ToHtml();
        var filled = Ui.Card.Filled["x"].ToHtml();

        Assert.Contains("after:inset-ring-white/25", lit, StringComparison.Ordinal);
        Assert.DoesNotContain("after:", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("after:", filled, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inset_divider_is_marked_and_stops_the_headers_line_at_the_content()
    {
        var card = Ui.Card.Divided.Divider(Ui.CardDivider.Inset)[Ui.CardHeader[Ui.CardHeading["Orders"]]];

        var html = card.ToHtml();

        Assert.Contains("data-ui-card-divider=\"inset\"", html, StringComparison.Ordinal);
        Assert.Contains("after:inset-x-6", html, StringComparison.Ordinal);
        Assert.Contains("border-transparent", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_divided_card_draws_a_line_under_its_header_and_over_its_footer()
    {
        var card = Ui.Card.Divided[Ui.CardHeader["h"], Ui.CardBody["b"], Ui.CardFooter["f"]];

        var html = card.ToHtml();

        Assert.Contains("border-b border-zinc-900/5", html, StringComparison.Ordinal);
        Assert.Contains("border-t border-zinc-900/5", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-card-divider", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_separated_card_tints_its_header_and_footer()
    {
        var card = Ui.Card.Separated[Ui.CardHeader["h"], Ui.CardBody["b"], Ui.CardFooter["f"]];

        var html = card.ToHtml();

        Assert.Equal(2, Occurrences(html, "bg-zinc-900/3 dark:bg-black/15"));
    }

    [Fact]
    public void An_inset_body_is_raised_on_a_tinted_card_and_a_recessed_well_on_a_white_one()
    {
        var tinted = Ui.Card.Inset.Soft[Ui.CardBody["b"]].ToHtml();
        var white = Ui.Card.Inset[Ui.CardBody["b"]].ToHtml();

        Assert.Contains("bg-white shadow-xs", tinted, StringComparison.Ordinal);
        Assert.Contains("bg-zinc-50", white, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-zinc-50", tinted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flush_body_runs_over_the_cards_own_border()
    {
        var card = Ui.Card.Flush.Muted[Ui.CardBody["b"]];

        var html = card.ToHtml();

        Assert.Contains("-mx-px", html, StringComparison.Ordinal);
        Assert.Contains("last:-mb-px", html, StringComparison.Ordinal);
        Assert.Contains("rounded-(--ui-card-radius)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seamless_body_adds_nothing_of_its_own_to_draw()
    {
        var card = Ui.Card[Ui.CardBody["b"]];

        var body = Part(card.ToHtml(), "data-ui-card-body>");

        Assert.DoesNotContain(" p-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("border", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_takes_the_cards_size_and_treatment()
    {
        var roomy = Ui.Card.Separated[Ui.CardHeader["h"]].ToHtml();
        var compact = Ui.Card.Separated.Xs[Ui.CardHeader.Lg["h"]].ToHtml();

        Assert.Contains("px-6 py-4", roomy, StringComparison.Ordinal);
        Assert.Contains("px-4 py-3", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_inside_a_body_is_bare_whatever_the_cards_treatment()
    {
        var card = Ui.Card.Separated[Ui.CardBody[Ui.CardHeader["sub"]]];

        var header = Part(card.ToHtml(), "data-ui-card-standalone>");

        Assert.Contains("pb-6", header, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-zinc-900/3", header, StringComparison.Ordinal);
        Assert.DoesNotContain("px-6", header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_outside_a_card_is_spaced_by_its_own_size()
    {
        var beside = Ui.CardHeader["Security"].ToHtml();
        var tight = Ui.CardHeader.Sm["Security"].ToHtml();

        Assert.Contains("pt-1 pb-5", beside, StringComparison.Ordinal);
        Assert.Contains("pt-1 pb-3", tight, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-header data-ui-card-standalone", beside, StringComparison.Ordinal);
    }

    [Fact]
    public void A_footer_mirrors_the_header()
    {
        var card = Ui.Card.Inset[Ui.CardFooter["f"]];

        var footer = Part(card.ToHtml(), "data-ui-card-standalone>");

        Assert.Contains("data-ui-card-footer", footer, StringComparison.Ordinal);
        Assert.Contains("px-5 pt-4 pb-3", footer, StringComparison.Ordinal);
        Assert.Contains("rounded-b-", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_is_a_div_until_it_is_given_a_level()
    {
        var plain = Ui.CardHeading["Orders"].ToHtml();
        var second = Ui.CardHeading.Level(2)["Orders"].ToHtml();

        Assert.StartsWith("<div ", plain, StringComparison.Ordinal);
        Assert.StartsWith("<h2 ", second, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-heading-size=\"base\"", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void A_headings_line_is_as_tall_as_the_cards_size_makes_it()
    {
        var roomy = Ui.Card[Ui.CardHeader[Ui.CardHeading["Orders"]]].ToHtml();
        var tight = Ui.Card.Sm[Ui.CardHeader[Ui.CardHeading["Orders"]]].ToHtml();
        var large = Ui.CardHeading.Lg["Security"].ToHtml();

        Assert.Contains("text-sm/6", roomy, StringComparison.Ordinal);
        Assert.Contains("text-sm/5", tight, StringComparison.Ordinal);
        Assert.Contains("text-base/6", large, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subheading_is_a_paragraph_under_the_heading()
    {
        var subheading = Ui.CardSubheading["Choose what you hear about"];

        var html = subheading.ToHtml();

        Assert.StartsWith("<p ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-card-subheading", html, StringComparison.Ordinal);
        Assert.Contains("mt-1 text-sm text-zinc-500", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Actions_tuck_into_a_headers_corner_and_centre_on_a_footers_row()
    {
        var card = Ui.Card.Separated[Ui.CardHeader[Ui.CardActions["a"]], Ui.CardFooter[Ui.CardActions["b"]]];

        var html = card.ToHtml();

        Assert.Contains("row-span-2 self-start -my-1 -me-3", html, StringComparison.Ordinal);
        Assert.Contains("self-center -my-1 -me-3", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.CardSize.Xs, "-my-1.5 -me-2.5")]
    [InlineData(Ui.CardSize.Sm, "-my-1.5 -me-1.5")]
    [InlineData(Ui.CardSize.Md, "-my-1 -me-3")]
    [InlineData(Ui.CardSize.Lg, "-my-1 -me-3")]
    public void Actions_sit_as_far_from_the_side_as_from_the_top_at_every_size(Ui.CardSize size, string margins)
    {
        var card = Ui.Card.Separated.Size(size)[Ui.CardHeader[Ui.CardActions["a"]]];

        var html = card.ToHtml();

        Assert.Contains(margins, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bleed_reaches_the_sides_always_and_the_top_and_bottom_only_at_the_ends()
    {
        var bleed = Ui.CardBleed[Img.Src("/forest.png").Alt("A misty forest path")];

        var html = bleed.ToHtml();

        Assert.Contains("data-ui-card-bleed", html, StringComparison.Ordinal);
        Assert.Contains("-mx-[calc(var(--ui-bleed-x)", html, StringComparison.Ordinal);
        Assert.Contains("first:-mt-[calc(var(--ui-bleed-top)", html, StringComparison.Ordinal);
        Assert.Contains("last:-mb-[calc(var(--ui-bleed-bottom)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_and_its_body_state_how_far_a_bleed_travels()
    {
        var card = Ui.Card.Inset[Ui.CardBody["b"]];

        var html = card.ToHtml();

        Assert.Contains("[--ui-bleed-x:--spacing(1)]", html, StringComparison.Ordinal);
        Assert.Contains("[--ui-bleed-x:19px]", html, StringComparison.Ordinal);
        Assert.Contains("[--ui-bleed-top:--spacing(6)]", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("card")]
    [InlineData("header")]
    [InlineData("heading")]
    [InlineData("subheading")]
    [InlineData("actions")]
    [InlineData("body")]
    [InlineData("footer")]
    [InlineData("bleed")]
    public void Every_part_takes_a_class_of_the_callers(string part)
    {
        const string mine = "callers-own";

        var html = part switch
        {
            "card" => Ui.Card.Class(mine)["x"].ToHtml(),
            "header" => Ui.CardHeader.Class(mine)["x"].ToHtml(),
            "heading" => Ui.CardHeading.Class(mine)["x"].ToHtml(),
            "subheading" => Ui.CardSubheading.Class(mine)["x"].ToHtml(),
            "actions" => Ui.CardActions.Class(mine)["x"].ToHtml(),
            "body" => Ui.CardBody.Class(mine)["x"].ToHtml(),
            "footer" => Ui.CardFooter.Class(mine)["x"].ToHtml(),
            _ => Ui.CardBleed.Class(mine)["x"].ToHtml(),
        };

        Assert.Contains(mine + "\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_part_writes_a_daisy_class_or_a_retired_token()
    {
        var card = Ui.Card.Inset.Soft[
            Ui.CardHeader[Ui.CardHeading["h"], Ui.CardSubheading["s"], Ui.CardActions["a"]],
            Ui.CardBody[Ui.CardBleed["m"]],
            Ui.CardFooter["f"]
        ];

        var html = card.ToHtml();

        Assert.DoesNotContain("base-", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ui-bg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("card-body", html.Replace("data-ui-card-body", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    // The opening tag that ends with `marker`, which is where a part's classes are.
    private static string Part(string html, string marker)
    {
        var end = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(end >= 0, $"no part marked {marker} in {html}");
        return html[html.LastIndexOf('<', end)..(end + marker.Length)];
    }

    private static int Occurrences(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;
}
