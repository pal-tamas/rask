using Rask.Core.Routing;

namespace Rask.Chrome.Tests;

// AppBar / TabStrip are the portable chrome vocabulary: one declaration that renders semantic markup on the
// web hosts and is projected to real platform bars inside a native shell. These cover the web half — the
// native projection is asserted in Rask.Native.Tests. Before they existed, a Screen's chrome slots could only
// be filled with Rask.Native components, so a shared Screen subclass forced a web app to reference the
// native package.
public partial class ChromeBarTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void AppBar_Empty_RendersTheBannerLandmark() =>
        Assert.Equal("""<div class="rask-header-bar" role="banner"></div>""", AppBar.ToHtml());

    [Fact]
    public void AppBar_Title_RendersItInTheTitleSlot() =>
        Assert.Equal(
            """<div class="rask-header-bar" role="banner"><div class="rask-header-title">Todos</div></div>""",
            AppBar.Title("Todos").ToHtml());

    [Fact]
    public void AppBar_EncodesTheTitle() =>
        Assert.Contains("&lt;script&gt;", AppBar.Title("<script>").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void AppBar_Trailing_RendersButtonsCarryingTheirIconToken()
    {
        var html = AppBar.Title("Todos")
            .Trailing([BarButton.Icon(BarIcon.Add).Title("New")])
            .ToHtml();

        Assert.Contains("""<div class="rask-header-trailing">""", html, StringComparison.Ordinal);
        Assert.Contains("""<button class="rask-bar-button" data-rask-icon="add" type="button">New</button>""",
            html, StringComparison.Ordinal);
    }

    // The label is the accessible name, so it has to reach the DOM as text. An icon-only look is a CSS
    // decision; dropping the text would make the button unusable with a screen reader.
    [Fact]
    public void BarButton_KeepsItsTitleAsText_SoTheButtonHasAnAccessibleName() =>
        Assert.Contains(">Save</button>", BarButton.Icon(BarIcon.Star).Title("Save").ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void TabStrip_NoTabs_RendersNothing() =>
        Assert.Equal(string.Empty, TabStrip.ToHtml());

    [Fact]
    public void TabStrip_RendersOneLinkPerTab_InTheNavigationLandmark()
    {
        var html = TabStrip.Tabs([
            TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
            TabItem.Title("Me").Icon(BarIcon.Person).To(new RouteUrl("/me")),
        ]).ToHtml();

        Assert.Contains("""<div class="rask-tab-bar" role="navigation">""", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/me\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-icon=\"person\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TabStrip_Badge_RendersBesideTheLabel() =>
        Assert.Contains("""<div class="rask-tab-badge">3</div>""",
            TabStrip.Tabs([TabItem.Title("Me").Icon(BarIcon.Person).To(new RouteUrl("/me")).Badge("3")]).ToHtml(),
            StringComparison.Ordinal);

    // aria-current="page" is what tells a screen reader which tab you are on; the active class is a visual
    // cue only, and on its own would leave the bar unusable without sight.
    [Fact]
    public void TabStrip_PinnedSelection_MarksThatTabActiveAndCurrent()
    {
        var html = TabStrip.Selected(1).Tabs([
            TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
            TabItem.Title("Me").Icon(BarIcon.Person).To(new RouteUrl("/me")),
        ]).ToHtml();

        Assert.Contains("class=\"rask-tab rask-tab-active\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\" href=\"/me\"", html, StringComparison.Ordinal);
        // Exactly one tab is current — an "active" class on two links is a real bug this pins down.
        Assert.Equal(1, CountOf(html, "rask-tab-active"));
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}

// The route-derived selection, which the native host reuses so both ends of one declaration light the same
// tab. Longest-prefix, not equality: a nested route has to keep its section's tab lit.
public partial class TabStripSelectionTests : global::Rask.Core.RaskMarkup
{
    private static readonly List<TabItem> Tabs =
    [
        TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
        TabItem.Title("Todos").Icon(BarIcon.List).To(new RouteUrl("/todos")),
        TabItem.Title("Me").Icon(BarIcon.Person).To(new RouteUrl("/me")),
    ];

    [Theory]
    [InlineData("/", 0)]
    [InlineData("/todos", 1)]
    [InlineData("/me", 2)]
    // A nested route keeps its section's tab lit rather than falling back to the root.
    [InlineData("/todos/42", 1)]
    [InlineData("/todos/42/edit", 1)]
    // "/todos" must not claim "/todos-archive" — a prefix match has to end on a segment boundary.
    [InlineData("/todos-archive", 0)]
    // Nothing matches beyond the root: a bar with no lit tab reads as broken, so the root wins.
    [InlineData("/unknown", 0)]
    [InlineData(null, 0)]
    public void DeriveSelected_PicksTheLongestMatchingTabPath(string? path, int expected) =>
        Assert.Equal(expected, global::Rask.Chrome.Components.TabStrip.DeriveSelected(Tabs, path));

    [Fact]
    public void DeriveSelected_NoTabs_IsZero() =>
        Assert.Equal(0, global::Rask.Chrome.Components.TabStrip.DeriveSelected(null, "/anything"));
}
