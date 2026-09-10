namespace Rask.Ui.Tests.Components;

/// <summary>
///     The theme picker, and the "System" row that makes every other row reversible.
/// </summary>
/// <remarks>
///     A reader who tries a palette out of curiosity is otherwise stuck with it: nothing in a list of
///     thirty-five names means "go back to what my computer says", and "light" is not that answer — it is
///     a third one that merely happens to match on a light machine.
/// </remarks>
public partial class UiThemePickerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_offers_the_system_row_first()
    {
        var html = UiThemePicker.ToHtml();
        var system = html.IndexOf($"value=\"{UiTheme.SystemValue}\"", StringComparison.Ordinal);
        var light = html.IndexOf("value=\"light\"", StringComparison.Ordinal);

        Assert.True(system >= 0, "the picker offers no way back to the operating system's preference.");
        Assert.True(light >= 0, "the picker lost its palettes.");
        Assert.True(system < light, "the system row is not first; it is the default state, so it leads.");
    }

    [Fact]
    public void The_system_row_carries_the_theme_controller_class()
    {
        // daisyUI compiled no rule that matches value="system", and that is deliberate — with it checked
        // nothing matches, so the CSS-only half falls back to the default palette on its own. But the
        // class is also what a host's delegated change listener recognises, so leaving it off would make
        // this the one row that reports nothing when a reader picks it.
        var row = Row(UiThemePicker.ToHtml(), UiTheme.SystemValue);

        Assert.Contains("theme-controller", row, StringComparison.Ordinal);
        Assert.Contains("type=\"radio\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void The_system_row_can_be_turned_off() =>
        Assert.DoesNotContain(
            $"value=\"{UiTheme.SystemValue}\"",
            UiThemePicker.ShowSystem(false).ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void The_system_row_takes_a_label()
    {
        var html = UiThemePicker.SystemLabel("Automatic").ToHtml();

        Assert.Contains("<span>Automatic</span>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Automatic\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void It_offers_every_palette_the_kit_ships()
    {
        var html = UiThemePicker.ToHtml();

        foreach (var theme in UiTheme.All)
        {
            Assert.Contains($"value=\"{UiTheme.Value(theme)}\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void It_never_offers_system_as_a_palette()
    {
        // UiTheme.All excludes it, so a narrowed list cannot smuggle it back in as a data-theme either.
        var html = UiThemePicker.Themes(UiTheme.All).ShowSystem(false).ToHtml();

        Assert.DoesNotContain($"value=\"{UiTheme.SystemValue}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Narrowing_the_palettes_keeps_the_system_row()
    {
        var html = UiThemePicker.Themes([UiThemeName.Light, UiThemeName.Dark]).ToHtml();

        Assert.Contains($"value=\"{UiTheme.SystemValue}\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"dark\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"dracula\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_is_keyed_by_its_own_value()
    {
        // RASK022 holds every list to identity rather than position, and the value is the identity here.
        var html = UiThemePicker.ToHtml();

        Assert.Contains("value=\"light\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"dark\"", html, StringComparison.Ordinal);
    }

    /// <summary>The markup of one row, so an assertion cannot pass on a different row's attributes.</summary>
    private static string Row(string html, string value)
    {
        var at = html.IndexOf($"value=\"{value}\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"no row with value=\"{value}\".");

        var start = html.LastIndexOf("<li", at, StringComparison.Ordinal);
        var end = html.IndexOf("</li>", at, StringComparison.Ordinal);

        return html[start..(end < 0 ? html.Length : end)];
    }
}
