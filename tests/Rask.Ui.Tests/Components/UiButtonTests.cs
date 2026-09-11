using System.Text.RegularExpressions;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The button's class composition, its two tags, and its icon-only form.
/// </summary>
/// <remarks>
///     Deriving from <c>RaskMarkup</c> is what makes <c>UiButton</c> here the chain's entry rather than
///     the type: a component's opening step only exists inside a markup host.
/// </remarks>
public partial class UiButtonTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_plain_button_carries_the_base_class_and_shows_its_children() =>
        Assert.Equal("<button class=\"btn\" type=\"button\">Save</button>", UiButton["Save"].ToHtml());

    [Theory]
    [InlineData(UiTone.Neutral, "btn-neutral")]
    [InlineData(UiTone.Primary, "btn-primary")]
    [InlineData(UiTone.Secondary, "btn-secondary")]
    [InlineData(UiTone.Accent, "btn-accent")]
    [InlineData(UiTone.Info, "btn-info")]
    [InlineData(UiTone.Success, "btn-success")]
    [InlineData(UiTone.Warning, "btn-warning")]
    [InlineData(UiTone.Error, "btn-error")]
    public void Every_tone_writes_its_own_class(UiTone tone, string expected) =>
        Assert.Contains(expected, UiButton.Tone(tone)["Save"].ToHtml());

    [Theory]
    [InlineData(UiVariant.Outline, "btn-outline")]
    [InlineData(UiVariant.Soft, "btn-soft")]
    [InlineData(UiVariant.Dash, "btn-dash")]
    [InlineData(UiVariant.Ghost, "btn-ghost")]
    [InlineData(UiVariant.Link, "btn-link")]
    public void Every_variant_writes_its_own_class(UiVariant variant, string expected) =>
        Assert.Contains(expected, UiButton.Variant(variant)["Save"].ToHtml());

    [Fact]
    public void Solid_is_the_absence_of_a_variant_class_rather_than_one_of_its_own() =>
        Assert.DoesNotContain("btn-solid", UiButton.Variant(UiVariant.Solid)["Save"].ToHtml());

    [Theory]
    [InlineData(UiSize.Xs, "btn-xs")]
    [InlineData(UiSize.Sm, "btn-sm")]
    [InlineData(UiSize.Md, "btn-md")]
    [InlineData(UiSize.Lg, "btn-lg")]
    [InlineData(UiSize.Xl, "btn-xl")]
    public void Every_size_writes_its_own_class(UiSize size, string expected) =>
        Assert.Contains(expected, UiButton.Size(size)["Save"].ToHtml());

    [Fact]
    public void Colour_fill_and_size_compose_rather_than_replacing_each_other()
    {
        // The three axes are independent, which is what lets an outlined error button exist without the
        // kit enumerating every pairing as a member of its own.
        var html = UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline).Size(UiSize.Lg)["Delete"].ToHtml();

        Assert.Contains("btn-error", html);
        Assert.Contains("btn-outline", html);
        Assert.Contains("btn-lg", html);
    }

    [Fact]
    public void Block_fills_its_container() =>
        Assert.Contains("btn-block", UiButton.Block(true)["Save"].ToHtml());

    [Fact]
    public void Wide_is_not_block() =>
        Assert.DoesNotContain("btn-block", UiButton.Wide(true)["Save"].ToHtml());

    [Fact]
    public void Wide_writes_its_own_class() =>
        Assert.Contains("btn-wide", UiButton.Wide(true)["Save"].ToHtml());

    [Fact]
    public void Active_draws_it_as_pressed() =>
        Assert.Contains("btn-active", UiButton.Active(true)["Filter"].ToHtml());

    [Fact]
    public void A_disabled_button_is_disabled_by_ATTRIBUTE_not_by_class()
    {
        // daisyUI has a `btn-disabled` class, and it styles without disabling: a button carrying only
        // that class still takes a click and still reaches its handler. The attribute is the one that
        // makes the browser refuse the interaction, which is what "disabled" has to mean.
        var html = UiButton.Disabled(true)["Save"].ToHtml();

        Assert.Contains(" disabled", html);
        Assert.DoesNotContain("btn-disabled", html);
    }

    [Theory]
    [InlineData("btn-square")]
    [InlineData("btn-circle")]
    public void An_icon_only_button_names_itself_to_a_screen_reader(string shape)
    {
        // A square holds one glyph, so its name cannot be visible text. It still has to be SOMEWHERE: a
        // button whose only content is a decorative icon is announced as "button", with no clue what it does.
        var html = shape == "btn-square"
            ? UiButton.AccessibleLabel("Close").Square(true)[UiIcon.Name(UiIconName.Close)].ToHtml()
            : UiButton.AccessibleLabel("Close").Circle(true)[UiIcon.Name(UiIconName.Close)].ToHtml();

        Assert.Contains(shape, html);
        Assert.Contains("aria-label=\"Close\"", html);
    }

    [Fact]
    public void An_aria_label_the_call_site_wrote_wins_over_the_accessible_label_and_is_written_once()
    {
        var html = UiButton.AccessibleLabel("Close").Aria(("label", "Close the dialog"))[UiIcon.Name(UiIconName.Close)]
            .ToHtml();

        Assert.Contains("aria-label=\"Close the dialog\"", html);
        Assert.Single(Regex.Matches(html, "aria-label="));
    }

    [Fact]
    public void An_accessible_label_keeps_the_call_sites_other_aria()
    {
        var html = UiButton.AccessibleLabel("Menu").Aria(("expanded", "false"))[UiIcon.Name(UiIconName.Menu)].ToHtml();

        Assert.Contains("aria-label=\"Menu\"", html);
        Assert.Contains("aria-expanded=\"false\"", html);
    }

    [Fact]
    public void An_ordinary_button_needs_no_aria_label() =>
        Assert.DoesNotContain("aria-label", UiButton["Save"].ToHtml());

    [Fact]
    public void An_icon_and_a_label_are_children_in_the_order_given()
    {
        var html = UiButton[UiIcon.Name(UiIconName.Check), "Save"].ToHtml();

        Assert.True(html.IndexOf("<svg", StringComparison.Ordinal) < html.IndexOf("Save</button>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_kit_stylesheet_sizes_an_icon_nobody_sized_by_the_button_it_sits_in()
    {
        // The icon is a child now, so the kit does not build it and cannot hand it `size-4` as the Icon
        // prop did. The compiled sheet has to carry the rule, keyed to UiIcon's DEFAULT pair so an icon a
        // call site sized on purpose is left alone.
        Assert.Matches(new Regex(@"\.btn\s*>\s*svg\.size-5\.shrink-0\s*\{[^}]*width:\s*1rem"), UiStylesheet.Css);
        Assert.Matches(new Regex(@"\.badge\s*>\s*svg\.size-5\.shrink-0\s*\{[^}]*width:\s*1em"), UiStylesheet.Css);
    }

    [Fact]
    public void Call_site_classes_are_added_to_the_kit_class_rather_than_replacing_it() =>
        Assert.Contains("class=\"btn mt-2\"", UiButton.Class("mt-2")["Save"].ToHtml());

    [Fact]
    public void Every_element_step_reaches_the_button_in_the_documented_order()
    {
        // None of these is declared on UiButton — they are Element's, which is the point of deriving from
        // it rather than mirroring a hand-picked few. Tag-specific attributes follow the universal ones.
        var html = UiButton
            .Id("save")
            .Data("testid", "save")
            .Role("switch")
            .TabIndex(0)
            .AccessibleLabel("Save the draft")
            .Type(UiButtonType.Submit)["Save"]
            .ToHtml();

        Assert.Equal(
            "<button id=\"save\" class=\"btn\" data-testid=\"save\" role=\"switch\" tabindex=\"0\" "
            + "aria-label=\"Save the draft\" type=\"submit\">Save</button>",
            html);
    }

    [Fact]
    public void Command_and_command_for_make_it_an_invoker()
    {
        var html = UiButton.Command("show-modal").CommandFor("confirm")["Delete"].ToHtml();

        Assert.Contains("command=\"show-modal\"", html, StringComparison.Ordinal);
        Assert.Contains("commandfor=\"confirm\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_an_href_it_is_an_anchor_rather_than_a_button()
    {
        // A link that looks like a button is an ordinary thing to want, and the alternative was a class
        // string in the application — the parallel vocabulary the kit exists to remove.
        var html = UiButton.Href("/docs")["Read the guide"].ToHtml();

        Assert.Equal("<a class=\"btn\" href=\"/docs\">Read the guide</a>", html);
    }

    [Fact]
    public void An_anchor_href_is_sanitised_as_cores_own_anchor_sanitises_it()
    {
        // The href is written by the kit now rather than by Core's A, so the sanitiser has to come with it:
        // a javascript: URL that reaches a kit button from data would otherwise run on click.
        var html = UiButton.Href("javascript:alert(1)")["Go"].ToHtml();

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(A.Href("javascript:alert(1)").ToHtml().Replace("<a ", "", StringComparison.Ordinal)
                .Replace("></a>", "", StringComparison.Ordinal),
            Regex.Match(html, "href=\"[^\"]*\"").Value);
    }

    [Fact]
    public void An_anchor_keeps_every_axis_the_button_has()
    {
        // The whole reason this is one component rather than two: the tone, fill and size axes are
        // identical either way, so a sibling component would duplicate all of them to change one tag.
        var html = UiButton
            .Href("/install")
            .Tone(UiTone.Primary)
            .Variant(UiVariant.Outline)
            .Size(UiSize.Sm)[UiIcon.Name(UiIconName.Download), "Install"]
            .ToHtml();

        Assert.StartsWith("<a class=\"btn btn-primary btn-outline btn-sm\"", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_anchor_is_never_disabled_and_never_an_invoker()
    {
        // There is no disabled state for a link in HTML. Faking one leaves it focusable and followable by
        // keyboard, which is worse than not offering it — a disabled link is a link you do not render.
        var html = UiButton.Href("/x").Disabled(true).Type(UiButtonType.Submit).Command("close")["Go"].ToHtml();

        Assert.DoesNotContain("disabled", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("command", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_tab_carries_the_rel_that_makes_it_safe()
    {
        // noopener is the attribute with no visible effect, so it is the one a caller forgets — a new tab
        // opened without it can reach back through window.opener.
        var html = UiButton.Href("https://example.test").NewTab(true)["Docs"].ToHtml();

        Assert.Contains("target=\"_blank\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void New_tab_without_an_href_changes_nothing() =>
        Assert.DoesNotContain("target", UiButton.NewTab(true)["Save"].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void An_icon_only_anchor_still_has_an_accessible_name() =>
        Assert.Contains(
            "aria-label=\"Settings\"",
            UiButton.Href("/settings").AccessibleLabel("Settings").Square(true)[UiIcon.Name(UiIconName.Gear)].ToHtml(),
            StringComparison.Ordinal);
}
