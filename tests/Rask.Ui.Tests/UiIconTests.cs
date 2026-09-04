using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Ui.Tests;

/// <summary>
///     The icon's class composition. These exist because an inline SVG, unlike the text glyph the
///     showcase drew before it, has no intrinsic size: an icon that loses its sizing utility renders as a
///     zero box. That failure is invisible to every cheap check — markup assertions see exactly the class
///     list the call site asked for, and a browser reports "element is not visible", which reads as a
///     layout or timing problem rather than as a missing width.
/// </summary>
public sealed class UiIconTests
{
    [Fact]
    public void An_icon_with_no_call_site_classes_carries_its_own_sizing()
    {
        Assert.Contains("size-5", ClassOf(UiIconName.Check, null));
    }

    [Theory]
    [InlineData("me-1")]
    [InlineData("me-2")]
    [InlineData("text-ui-muted")]
    [InlineData("nav-group-chevron")]
    public void Call_site_classes_are_added_to_the_sizing_not_substituted_for_it(string extra)
    {
        // The regression this pins: Class used to REPLACE the default, so every caller that passed only a
        // margin — over a hundred of them across the showcase — silently shipped a zero-sized icon.
        var classes = ClassOf(UiIconName.Check, extra);

        Assert.Contains("size-5", classes);
        Assert.Contains(extra, classes);
    }

    [Theory]
    [InlineData("size-4 shrink-0")]
    [InlineData("size-3.5")]
    [InlineData("md:size-4")]
    [InlineData("h-4 w-4")]
    public void A_call_site_that_names_a_size_replaces_the_default_rather_than_competing_with_it(string extra)
    {
        // Two size utilities on one element are resolved by stylesheet order, not by attribute order, so
        // keeping both would make the rendered size depend on how the sheet happened to be generated.
        var classes = ClassOf(UiIconName.Check, extra);

        Assert.DoesNotContain("size-5", classes);
        Assert.Equal(extra, classes);
    }

    [Fact]
    public void Every_name_in_the_set_draws_at_least_one_path()
    {
        // A name with no shape is a silently blank icon, and the set is large enough that adding a member
        // and forgetting its path data is an easy miss.
        foreach (var name in Enum.GetValues<UiIconName>())
        {
            Assert.True(Html(name, null).Contains("<path", StringComparison.Ordinal), $"{name} renders no path.");
        }
    }

    private static string Html(UiIconName name, string? cls) =>
        RaskTest.Render(new Host { IconName = name, IconClass = cls }).Html;

    private static string ClassOf(UiIconName name, string? cls) =>
        Regex.Match(Html(name, cls), "class=\"([^\"]*)\"").Groups[1].Value;

}
