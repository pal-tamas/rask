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
        // No <span> around the text any more: the content is the children the caller passed, exactly as
        // a plain <button> renders them. type="button" is still written, and deliberately - see the
        // WriteAttributes override on UiButton.
        Assert.Equal("<button class=\"btn\" type=\"button\">Save</button>",
            UiButton["Save"].ToHtml());

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
        var html = UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline).Size(UiSize.Lg)["Delete"]
            .ToHtml();

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

        Assert.Contains("disabled", html);
        Assert.DoesNotContain("btn-disabled", html);
    }

    [Theory]
    [InlineData("btn-square")]
    [InlineData("btn-circle")]
    public void An_icon_only_button_is_named_by_its_call_site(string shape)
    {
        // A square holds one glyph, so a name cannot be visible text, and it still has to be SOMEWHERE:
        // a button whose only content is a decorative icon is announced as "button" and nothing else.
        //
        // The kit used to supply it, from a required Label. It cannot any more — UiButton IS a
        // <button>, and an element renders the children it is given rather than inventing content — so
        // naming an icon-only button is the call site's job, exactly as it is for a plain <button>.
        // This test is what says so.
        var icon = UiIcon.Name(UiIconName.Close).Class("size-4");
        var html = shape == "btn-square"
            ? UiButton.Square(true).Aria(CloseAria)[icon].ToHtml()
            : UiButton.Circle(true).Aria(CloseAria)[icon].ToHtml();

        Assert.Contains(shape, html);
        Assert.Contains("aria-label=\"Close\"", html);
        Assert.DoesNotContain("<span>Close</span>", html);
    }

    private static readonly IReadOnlyDictionary<string, string?> CloseAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Close" };

    [Fact]
    public void An_ordinary_button_shows_its_children_and_needs_no_aria_label()
    {
        // Its text is its children, so it names itself the way any <button> with text does.
        var html = UiButton["Save"].ToHtml();

        Assert.Contains("Save", html);
        Assert.DoesNotContain("aria-label", html);
    }

    [Fact]
    public void The_call_site_orders_an_icon_against_the_text()
    {
        // Order used to be the component's to decide (icon, then label). It is the caller's now, which
        // is the point of children: a trailing chevron is written by putting it second.
        var html = UiButton[UiIcon.Name(UiIconName.Check).Class("size-4"), "Save"].ToHtml();

        Assert.Contains("size-4", html);
        Assert.True(html.IndexOf("<svg", StringComparison.Ordinal)
            < html.IndexOf("Save", StringComparison.Ordinal));
    }

    [Fact]
    public void Call_site_classes_are_added_to_the_kit_class_rather_than_replacing_it() =>
        Assert.Contains("btn mt-2", UiButton.Class("mt-2")["Save"].ToHtml());
}
