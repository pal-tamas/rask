namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Progress</c>: Flux UI's progress bar — what it announces, and how far the bar goes.
/// </summary>
public partial class UiProgressTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_progress_bar_announces_its_value_out_of_a_hundred()
    {
        var html = Ui.Progress.Value(75).ToHtml();

        Assert.StartsWith("<ui-progress ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-progress=\"\" role=\"progressbar\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-valuemin=\"0\" aria-valuenow=\"75\" aria-valuemax=\"100\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_is_as_wide_as_the_share_of_the_value_in_the_maximum()
    {
        var html = Ui.Progress.Value(3).Max(7).ToHtml();

        Assert.Contains("--ui-progress:42.857142857142854;--ui-progress-percentage:42.857142857142854%", html, StringComparison.Ordinal);
        Assert.Contains("style=\"width:var(--ui-progress-percentage)\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-valuenow=\"3\" aria-valuemax=\"7\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(150d, 100d, "100")]
    [InlineData(-20d, 100d, "0")]
    [InlineData(5d, 0d, "100")]
    [InlineData(0d, 0d, "0")]
    [InlineData(50d, -10d, "0")]
    public void The_bar_stops_at_empty_and_at_full_while_the_value_is_announced_as_given(double value, double max, string share)
    {
        var html = Ui.Progress.Value(value).Max(max).ToHtml();

        Assert.Contains($"--ui-progress:{share};--ui-progress-percentage:{share}%", html, StringComparison.Ordinal);
        Assert.Contains($"aria-valuenow=\"{value}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_progress_bar_with_no_value_is_empty()
    {
        var html = Ui.Progress.ToHtml();

        Assert.Contains("--ui-progress:0;--ui-progress-percentage:0%", html, StringComparison.Ordinal);
        Assert.Contains("aria-valuenow=\"0\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_is_the_accent_until_a_colour_is_named()
    {
        var accent = Ui.Progress.Value(75).ToHtml();
        var purple = Ui.Progress.Value(75).Color(Ui.Color.Purple).ToHtml();

        Assert.Contains("bg-fx-accent", accent, StringComparison.Ordinal);
        Assert.Contains("bg-purple-600 dark:bg-purple-400", purple, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-fx-accent", purple, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void Every_colour_fills_the_bar_with_a_class_the_sheet_defines(Ui.Color color)
    {
        var html = Ui.Progress.Value(50).Color(color).ToHtml();

        var hue = color.ToString().ToLowerInvariant();
        Assert.Contains($"bg-{hue}-600 dark:bg-{hue}-400", html, StringComparison.Ordinal);
        Assert.Contains($".bg-{hue}-600", UiStylesheet.Css, StringComparison.Ordinal);
        Assert.Contains($"bg-{hue}-400", UiStylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_site_names_it_and_sizes_it_and_keeps_the_kit_own_attributes()
    {
        var html = Ui.Progress.Value(42).Class("h-3").Style("max-width:20rem").Aria("label", "Upload").ToHtml();

        Assert.Contains("[:where(&amp;)]:h-1.5", html, StringComparison.Ordinal);
        Assert.Contains("rounded-full bg-zinc-200 dark:bg-white/10 h-3\"", html, StringComparison.Ordinal);
        Assert.Contains("--ui-progress-percentage:42%;max-width:20rem", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Upload\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_moves_to_its_new_width_rather_than_jumping()
    {
        var html = Ui.Progress.Value(10).ToHtml();

        Assert.Contains("transition-[width] duration-300 ease-out", html, StringComparison.Ordinal);
    }

    public static TheoryData<Ui.Color> Colors() => [.. Enum.GetValues<Ui.Color>()];
}
