using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux UI's slider: a real range input per thumb, laid over the track that thumb can reach.
/// </summary>
/// <remarks>
///     What the browser does with that input — dragging, a press on the track, the arrow keys — is measured
///     against Flux's page by hand and in the site's browser suite. Here: what is written for a value, and what a
///     reported value writes back.
/// </remarks>
public partial class UiSliderTests : global::Rask.Core.RaskMarkup
{
    private const string Thumb = "var(--ui-slider-thumb)";

    [Fact]
    public void A_slider_is_one_native_range_input_inside_its_thumb()
    {
        var html = Ui.Slider.Value(30).Min(10).Max(90).Step(5).ToHtml();

        Assert.Equal(1, Occurrences(html, "<input"));
        Assert.Matches("data-ui-slider-thumb[^>]*><input", html);
        Assert.Contains("type=\"range\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"30\"", html, StringComparison.Ordinal);
        Assert.Contains("min=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"90\"", html, StringComparison.Ordinal);
        Assert.Contains("step=\"5\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void It_runs_from_0_to_100_in_steps_of_1_unless_told_otherwise()
    {
        var html = Ui.Slider.Value(30).ToHtml();

        Assert.Contains("min=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"100\"", html, StringComparison.Ordinal);
        Assert.Contains("step=\"1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_parts_carry_the_markers_Flux_gives_them()
    {
        var html = Ui.Slider.Value(30).ToHtml();

        Assert.Contains("data-ui-slider=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-control", html, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(html, "data-ui-slider-track"));
        Assert.Contains("data-ui-slider-indicator", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-slider-thumb", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_thumb_and_the_fill_sit_where_the_value_is()
    {
        // A quarter of the way along the thumb's travel — the track less one thumb — and half a thumb in.
        var at = $"calc({Thumb} / 2 + 0.25 * (100% - {Thumb}))";

        var html = System.Net.WebUtility.HtmlDecode(Ui.Slider.Value(25).ToHtml());

        Assert.Contains($"inset-inline-start:{at}", html, StringComparison.Ordinal);
        Assert.Contains($"inset-inline-start:0;width:{at}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bound_slider_writes_the_value_back_while_the_thumb_moves()
    {
        var model = new Mixer { Volume = 40 };
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Volume));

        var html = await page.On("input").Input("70");

        Assert.Equal(70, model.Volume);
        Assert.Contains("value=\"70\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_controlled_slider_reports_the_value_and_keeps_its_own()
    {
        int? reported = null;
        var page = Page.Render(() => Ui.Slider.Value(40).OnChange(value => { reported = value; }));

        var html = await page.On("input").Input("70");

        Assert.Equal(70, reported);
        Assert.Contains("value=\"40\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("73", 70)]
    [InlineData("76", 80)]
    [InlineData("999", 100)]
    [InlineData("-5", 0)]
    public async Task A_reported_value_is_held_to_the_step_and_the_ends(string reported, int expected)
    {
        var model = new Mixer { Volume = 40 };
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Volume).Step(10));

        await page.On("input").Input(reported);

        Assert.Equal(expected, model.Volume);
    }

    [Fact]
    public async Task A_slider_over_a_decimal_keeps_a_fractional_step_exact()
    {
        var model = new Mixer { Gain = 0.1m };
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Gain).Max(1).Step(0.1));

        var html = await page.On("input").Input("0.3");

        Assert.Equal(0.3m, model.Gain);
        Assert.Contains("step=\"0.1\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_is_two_thumbs_over_an_array_of_two()
    {
        var html = System.Net.WebUtility.HtmlDecode(Ui.Slider.Value(new[] { 20, 80 }).Range().ToHtml());

        Assert.Equal(2, Occurrences(html, "data-ui-slider-thumb"));
        Assert.Contains("aria-valuetext=\"20 start range\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-valuetext=\"80 end range\"", html, StringComparison.Ordinal);
        Assert.Contains($"inset-inline-start:calc({Thumb} / 2 + 0.2 * (100% - {Thumb}));width:calc(0.6 * (100% - {Thumb}))", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_thumb_of_a_range_stops_at_the_other()
    {
        // The browser enforces it: the first input's max is the second thumb, the second's min the first.
        var thumbs = Inputs(Ui.Slider.Value(new[] { 20, 80 }).Range().ToHtml());

        Assert.Contains("min=\"0\"", thumbs[0], StringComparison.Ordinal);
        Assert.Contains("max=\"80\"", thumbs[0], StringComparison.Ordinal);
        Assert.Contains("min=\"20\"", thumbs[1], StringComparison.Ordinal);
        Assert.Contains("max=\"100\"", thumbs[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Min_steps_between_keeps_the_thumbs_that_many_steps_apart()
    {
        var thumbs = Inputs(Ui.Slider.Value(new[] { 450, 550 }).Range().Max(1000).Step(10).MinStepsBetween(5).ToHtml());

        Assert.Contains("max=\"500\"", thumbs[0], StringComparison.Ordinal);
        Assert.Contains("min=\"500\"", thumbs[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_one_thumb_of_a_bound_range_writes_a_new_array()
    {
        var model = new Mixer();
        var before = model.Band;
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Band).Range());

        await page.On("input[aria-valuetext=\"80 end range\"]").Input("60");

        Assert.Equal([20, 60], model.Band);
        Assert.NotSame(before, model.Band);
    }

    [Fact]
    public async Task A_thumb_reported_past_its_neighbour_stops_at_it()
    {
        var model = new Mixer();
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Band).Range());

        await page.On("input[aria-valuetext=\"20 start range\"]").Input("95");

        Assert.Equal([80, 80], model.Band);
    }

    [Fact]
    public void Range_over_a_single_number_says_what_it_needs()
    {
        var slider = Ui.Slider.Value(20).Range();

        var thrown = Assert.Throws<InvalidOperationException>(() => slider.ToHtml());

        Assert.Contains("array of two", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tick_says_whether_the_fill_reaches_it_and_whether_the_thumb_is_on_it()
    {
        var html = Ui.Slider.Value(3).Min(1).Max(5)[
            Ui.SliderTick.Value(2),
            Ui.SliderTick.Value(3),
            Ui.SliderTick.Value(4)
        ].ToHtml();

        Assert.Contains("data-ui-slider-tick-position=\"below\"", html, StringComparison.Ordinal);
        Assert.Matches("data-ui-slider-tick=\"\" data-value=\"2\" data-active=\"\"", html);
        Assert.Matches("data-ui-slider-tick=\"\" data-value=\"3\" data-current=\"\" data-active=\"\"", html);
        Assert.Contains("data-ui-slider-tick=\"\" data-value=\"4\"><span", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tick_draws_a_line_a_dot_or_its_label()
    {
        var html = Ui.Slider.Value(3).Min(1).Max(5).Inside[
            Ui.SliderTick.Value(1),
            Ui.SliderTick.Value(3).Dot,
            Ui.SliderTick.Value(5)["High"]
        ].ToHtml();

        Assert.Contains("data-ui-slider-tick-position=\"inside\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "data-ui-slider-tick-line"));
        Assert.Equal(1, Occurrences(html, "data-ui-slider-tick-dot"));
        Assert.Contains(">High</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pressing_a_tick_moves_the_thumb_to_its_value()
    {
        var model = new Mixer { Volume = 1 };
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Volume).Min(1).Max(5)[
            Ui.SliderTick.Value(2),
            Ui.SliderTick.Value(4)
        ]);

        await page.On("[data-value=\"4\"]").Click();

        Assert.Equal(4, model.Volume);
    }

    [Fact]
    public async Task Pressing_a_tick_of_a_range_moves_the_nearer_thumb()
    {
        var model = new Mixer();
        var page = Page.Render(() => Ui.Slider.Bind(() => model.Band).Range()[
            Ui.SliderTick.Value(30),
            Ui.SliderTick.Value(70)
        ]);

        await page.On("[data-value=\"70\"]").Click();

        Assert.Equal([20, 70], model.Band);
    }

    [Theory]
    [InlineData("size-6", "--ui-slider-thumb:calc(0.25rem * 6)")]
    [InlineData("shadow-lg size-[22px]", "--ui-slider-thumb:22px")]
    public void A_resized_thumb_tells_the_fill_and_the_ticks_its_size(string thumbClass, string expected) =>
        Assert.Contains(expected, Ui.Slider.Value(30).ThumbClass(thumbClass).ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Track_and_thumb_classes_land_on_the_track_and_the_thumb()
    {
        var html = Ui.Slider.Value(30).TrackClass("h-5").ThumbClass("size-5").ToHtml();

        Assert.Matches("class=\"[^\"]* h-5\" data-ui-slider-track", html);
        Assert.Matches("class=\"[^\"]* size-5\" [^>]*data-ui-slider-thumb", html);
    }

    [Fact]
    public void A_disabled_slider_disables_its_input_and_takes_no_tick_presses()
    {
        var html = Ui.Slider.Value(3).Min(1).Max(5).Disabled()[Ui.SliderTick.Value(2)].ToHtml();

        Assert.Equal(1, Marked(html, "disabled"));
        Assert.DoesNotContain("data-rask-on-click", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_s_label_names_the_input_as_Flux_does()
    {
        var html = Ui.Field[Ui.Label["Corner radius"], Ui.Slider.Value(30).Id("radius")].ToHtml();

        Assert.Matches("<label [^>]*for=\"radius\"", html);
        Assert.Matches("<input [^>]*id=\"radius\"", html);
        Assert.Contains("aria-labelledby=\"radius-label\"", Inputs(html)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Both_thumbs_of_a_range_in_a_field_are_named_by_its_label()
    {
        var html = Ui.Field[Ui.Label["Price"], Ui.Slider.Value(new[] { 20, 80 }).Range().Id("price")].ToHtml();

        Assert.Equal(2, Occurrences(html, "aria-labelledby=\"price-label\""));
    }

    [Fact]
    public void A_big_step_is_handed_to_the_runtime_on_the_input()
    {
        var html = Ui.Slider.Value(500).Max(1000).Step(1).BigStep(100).ToHtml();

        Assert.Contains("data-rask-big-step=\"100\"", Inputs(html)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Both_thumbs_of_a_range_carry_the_big_step()
    {
        var html = Ui.Slider.Value(new[] { 200, 800 }).Range().Max(990).Step(10).BigStep(100).ToHtml();

        Assert.Equal(2, Occurrences(html, "data-rask-big-step=\"100\""));
    }

    [Fact]
    public void Without_a_big_step_the_page_keys_stay_the_browser_s()
    {
        var html = Ui.Slider.Value(50).ToHtml();

        Assert.DoesNotContain("data-rask-big-step", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var (count, at) = (0, haystack.IndexOf(needle, StringComparison.Ordinal));
        while (at >= 0)
        {
            (count, at) = (count + 1, haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal));
        }

        return count;
    }

    // Every <input> tag, with what the serializer encoded (a `+` in a calc()) decoded again.
    private static List<string> Inputs(string html) =>
        [.. InputTag().Matches(System.Net.WebUtility.HtmlDecode(html)).Select(tag => tag.Value)];

    private static int Marked(string html, string attribute) =>
        System.Text.RegularExpressions.Regex.Matches(html, $"\\s{attribute}[\\s=/>]").Count;

    [System.Text.RegularExpressions.GeneratedRegex("<input[^>]*>")]
    private static partial System.Text.RegularExpressions.Regex InputTag();

    private sealed class Mixer
    {
        public int Volume { get; set; }

        public decimal Gain { get; set; }

        public int[] Band { get; set; } = [20, 80];
    }
}
