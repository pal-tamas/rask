using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's carousel: the markup a browser scrolls and the runtime's carousel hook reads, and what each prop writes on it.
/// </summary>
public partial class UiCarouselTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_carousel_is_a_labelled_group_around_a_snapping_track_of_numbered_slides()
    {
        var carousel = Ui.Carousel[Ui.CarouselSlide.Class("w-full")["one"], Ui.CarouselSlide.Class("w-full")["two"]];

        var html = carousel.ToHtml();

        Assert.StartsWith("<div class=\"[:where(&amp;)]:block\" data-ui-carousel data-rask-carousel data-at-start role=\"group\" aria-roledescription=\"carousel\" aria-label=\"Items\">", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"relative\" data-ui-carousel-viewport>", html, StringComparison.Ordinal);
        Assert.Contains("overflow-x-auto overflow-y-hidden overscroll-x-contain snap-x [scrollbar-width:none] snap-proximity scroll-smooth\" data-ui-carousel-track data-rask-carousel-track aria-live=\"polite\" aria-atomic=\"false\">", html, StringComparison.Ordinal);
        Assert.Contains("shrink-0 snap-start w-full\" data-ui-carousel-slide data-selected role=\"group\" aria-roledescription=\"slide\" aria-label=\"1 of 2\">one</div>", html, StringComparison.Ordinal);
        Assert.Contains("shrink-0 snap-start w-full\" data-ui-carousel-slide role=\"group\" aria-roledescription=\"slide\" aria-label=\"2 of 2\">two</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Slides_handed_over_as_a_list_are_numbered_like_slides_written_out()
    {
        var names = new[] { "one", "two", "three" };
        var carousel = Ui.Carousel[names.Select(name => Ui.CarouselSlide.Key(name)[name])];

        var html = carousel.ToHtml();

        Assert.Contains("aria-label=\"1 of 3\">one", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"3 of 3\">three", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_slide_that_arrives_renumbers_the_ones_already_there()
    {
        var names = new List<string> { "one", "two" };
        var page = Page.Render(() => Ui.Carousel[names.Select(name => Ui.CarouselSlide.Key(name)[name])]);

        names.Add("three");
        page.Render();

        Assert.Contains("aria-label=\"1 of 3\">one", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"3 of 3\">three", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_arrows_are_there_unless_turned_off_and_the_previous_one_starts_out_of_use()
    {
        var with = Ui.Carousel[Ui.CarouselSlide["one"]].ToHtml();
        var without = Ui.Carousel.Arrows(false)[Ui.CarouselSlide["one"]].ToHtml();

        Assert.Matches("data-direction=\"previous\" data-at-start[^>]*><button [^>]*aria-label=\"Previous slide\"[^>]* disabled[ >]", with);
        Assert.Matches("data-direction=\"next\" data-at-start[^>]*><button [^>]*aria-label=\"Next slide\"", with);
        Assert.DoesNotMatch("aria-label=\"Next slide\"[^>]* disabled[ >]", with);
        Assert.Contains("start-3", with, StringComparison.Ordinal);
        Assert.Contains("end-3", with, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", without, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.CarouselArrowsPosition.Overlap, "start-0 -translate-x-1/2", "end-0 translate-x-1/2")]
    [InlineData(Ui.CarouselArrowsPosition.Outside, "end-full me-3", "start-full ms-3")]
    public void An_arrow_position_moves_both_arrows_against_the_viewport(Ui.CarouselArrowsPosition position, string previous, string next)
    {
        var carousel = Ui.Carousel.ArrowsPosition(position).ArrowsClass("mine")[Ui.CarouselSlide["one"]];

        var html = carousel.ToHtml();

        Assert.Contains(previous, html, StringComparison.Ordinal);
        Assert.Contains(next, html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split(" mine\"").Length - 1);
    }

    [Fact]
    public void Indicators_are_one_named_button_per_slide_with_the_first_current()
    {
        var carousel = Ui.Carousel.Indicators()[Ui.CarouselSlide["one"], Ui.CarouselSlide["two"], Ui.CarouselSlide["three"]];

        var html = carousel.ToHtml();

        Assert.Contains("data-rask-carousel-indicators role=\"group\" aria-label=\"Choose slide to display\">", html, StringComparison.Ordinal);
        Assert.Matches("<button [^>]*data-selected[^>]*aria-label=\"Show slide 1\" aria-current=\"true\"", html);
        Assert.Contains("aria-label=\"Show slide 3\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Show slide 4", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_takes_the_arrows_and_the_indicators_out_of_use_and_leaves_the_track_alone()
    {
        var carousel = Ui.Carousel.Indicators().Disabled()[Ui.CarouselSlide["one"], Ui.CarouselSlide["two"]];

        var html = carousel.ToHtml();

        Assert.Equal(4, System.Text.RegularExpressions.Regex.Count(html, "<button [^>]* disabled[ >]"));
        Assert.Contains("overflow-x-auto", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Snap_and_scroll_are_the_tracks_own_scroll_snap_and_scroll_behaviour()
    {
        var carousel = Ui.Carousel.Mandatory.Instant.TrackClass("rounded-lg")[Ui.CarouselSlide["one"]];

        var html = carousel.ToHtml();

        Assert.Contains("snap-mandatory scroll-auto rounded-lg\" data-ui-carousel-track", html, StringComparison.Ordinal);
        Assert.Contains("data-scroll=\"instant\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Bleed_pulls_the_carousel_out_by_the_gutter_and_pads_the_track_back_in()
    {
        var carousel = Ui.Carousel.Bleed()[Ui.CarouselSlide["one"]];

        var html = carousel.ToHtml();

        Assert.StartsWith("<div class=\"[:where(&amp;)]:block [:where(&amp;)]:-mx-[var(--ui-bleed,1.5rem)]\"", html, StringComparison.Ordinal);
        Assert.Contains("px-[var(--ui-bleed,1.5rem)] scroll-px-[var(--ui-bleed,1.5rem)]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Fade_marks_the_track_for_the_stylesheets_scroll_driven_mask()
    {
        var carousel = Ui.Carousel.Fade()[Ui.CarouselSlide["one"]];

        var html = carousel.ToHtml();

        Assert.Contains("data-ui-carousel-track data-rask-carousel-track data-fade", html, StringComparison.Ordinal);
        Assert.Contains("[data-ui-carousel-track][data-fade]", UiStylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_runtime_acts_on_is_written_on_the_root_for_it()
    {
        var carousel = Ui.Carousel.Name("stays").Page.Rewind.Autoplay().AutoplayInterval(3500)[Ui.CarouselSlide["one"]];

        var html = carousel.ToHtml();

        Assert.Contains("data-name=\"stays\" data-advance=\"page\" data-wrap=\"rewind\" data-autoplay=\"3500\"", html, StringComparison.Ordinal);
        Assert.Contains("data-autoplay=\"5000\"", Ui.Carousel.Autoplay()[Ui.CarouselSlide["one"]].ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void External_controls_name_their_carousel_and_dim_rather_than_leave()
    {
        var controls = Ui.CarouselControls.Name("stays");

        var html = controls.ToHtml();

        Assert.Contains(" data-ui-carousel-controls data-rask-carousel-controls data-name=\"stays\">", html, StringComparison.Ordinal);
        Assert.Contains("data-at-start:opacity-50", html, StringComparison.Ordinal);
        Assert.Contains("data-at-end:opacity-50", html, StringComparison.Ordinal);
        Assert.Matches("aria-label=\"Previous slide\"[^>]* disabled[ >]", html);
    }

    [Fact]
    public void Daisys_carousel_rules_are_gone_from_the_kits_sheet()
    {
        var css = UiStylesheet.Css;

        var daisy = css.Contains(".carousel-item", StringComparison.Ordinal) || css.Contains(".swap-active", StringComparison.Ordinal);

        Assert.False(daisy, "daisyUI's carousel or swap rules are back in the compiled sheet.");
    }
}
