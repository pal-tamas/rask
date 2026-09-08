namespace Rask.Ui.Tests.Components;

/// <summary>
///     The button's class composition and its icon-only form.
/// </summary>
/// <remarks>
///     Deriving from <c>RaskMarkup</c> is what makes <c>UiButton</c> here the chain's entry rather than
///     the type: a component's opening step only exists inside a markup host.
/// </remarks>
public partial class UiButtonTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_plain_button_carries_the_base_class_and_nothing_else() =>
        Assert.Equal("<button class=\"btn\" type=\"button\"><span>Save</span></button>",
            UiButton.Label("Save").ToHtml());

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
        Assert.Contains(expected, UiButton.Label("Save").Tone(tone).ToHtml());

    [Theory]
    [InlineData(UiVariant.Outline, "btn-outline")]
    [InlineData(UiVariant.Soft, "btn-soft")]
    [InlineData(UiVariant.Dash, "btn-dash")]
    [InlineData(UiVariant.Ghost, "btn-ghost")]
    [InlineData(UiVariant.Link, "btn-link")]
    public void Every_variant_writes_its_own_class(UiVariant variant, string expected) =>
        Assert.Contains(expected, UiButton.Label("Save").Variant(variant).ToHtml());

    [Fact]
    public void Solid_is_the_absence_of_a_variant_class_rather_than_one_of_its_own() =>
        Assert.DoesNotContain("btn-solid", UiButton.Label("Save").Variant(UiVariant.Solid).ToHtml());

    [Theory]
    [InlineData(UiSize.Xs, "btn-xs")]
    [InlineData(UiSize.Sm, "btn-sm")]
    [InlineData(UiSize.Md, "btn-md")]
    [InlineData(UiSize.Lg, "btn-lg")]
    [InlineData(UiSize.Xl, "btn-xl")]
    public void Every_size_writes_its_own_class(UiSize size, string expected) =>
        Assert.Contains(expected, UiButton.Label("Save").Size(size).ToHtml());

    [Fact]
    public void Colour_fill_and_size_compose_rather_than_replacing_each_other()
    {
        // The three axes are independent, which is what lets an outlined error button exist without the
        // kit enumerating every pairing as a member of its own.
        var html = UiButton.Label("Delete").Tone(UiTone.Error).Variant(UiVariant.Outline).Size(UiSize.Lg)
            .ToHtml();

        Assert.Contains("btn-error", html);
        Assert.Contains("btn-outline", html);
        Assert.Contains("btn-lg", html);
    }

    [Fact]
    public void Block_fills_its_container() =>
        Assert.Contains("btn-block", UiButton.Label("Save").Block(true).ToHtml());

    [Fact]
    public void Wide_is_not_block() =>
        Assert.DoesNotContain("btn-block", UiButton.Label("Save").Wide(true).ToHtml());

    [Fact]
    public void Wide_writes_its_own_class() =>
        Assert.Contains("btn-wide", UiButton.Label("Save").Wide(true).ToHtml());

    [Fact]
    public void Active_draws_it_as_pressed() =>
        Assert.Contains("btn-active", UiButton.Label("Filter").Active(true).ToHtml());

    [Fact]
    public void A_disabled_button_is_disabled_by_ATTRIBUTE_not_by_class()
    {
        // daisyUI has a `btn-disabled` class, and it styles without disabling: a button carrying only
        // that class still takes a click and still reaches its handler. The attribute is the one that
        // makes the browser refuse the interaction, which is what "disabled" has to mean.
        var html = UiButton.Label("Save").Disabled(true).ToHtml();

        Assert.Contains("disabled", html);
        Assert.DoesNotContain("btn-disabled", html);
    }

    [Theory]
    [InlineData("btn-square")]
    [InlineData("btn-circle")]
    public void An_icon_only_button_names_itself_to_a_screen_reader(string shape)
    {
        // A square holds one glyph, so the label cannot be visible text. It still has to be SOMEWHERE:
        // a button whose only content is a decorative icon is announced as "button", with no clue what
        // it does. It becomes the accessible name instead.
        var html = shape == "btn-square"
            ? UiButton.Label("Close").Square(true).Icon(UiIconName.Close).ToHtml()
            : UiButton.Label("Close").Circle(true).Icon(UiIconName.Close).ToHtml();

        Assert.Contains(shape, html);
        Assert.Contains("aria-label=\"Close\"", html);
        Assert.DoesNotContain("<span>Close</span>", html);
    }

    [Fact]
    public void An_ordinary_button_shows_its_label_and_needs_no_aria_label()
    {
        var html = UiButton.Label("Save").ToHtml();

        Assert.Contains("<span>Save</span>", html);
        Assert.DoesNotContain("aria-label", html);
    }

    [Fact]
    public void An_icon_sits_before_the_label_and_carries_its_own_size()
    {
        var html = UiButton.Label("Save").Icon(UiIconName.Check).ToHtml();

        Assert.Contains("size-4", html);
        Assert.True(html.IndexOf("<svg", StringComparison.Ordinal)
            < html.IndexOf("<span>Save</span>", StringComparison.Ordinal));
    }

    [Fact]
    public void Call_site_classes_are_added_to_the_kit_class_rather_than_replacing_it() =>
        Assert.Contains("btn mt-2", UiButton.Label("Save").Class("mt-2").ToHtml());
}
