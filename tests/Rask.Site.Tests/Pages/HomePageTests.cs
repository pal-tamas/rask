using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.Site.Tests.Pages;

/// <summary>
/// The landing page pitches what is in the box, not a benchmark against another framework.
/// </summary>
/// <remarks>
/// The first thing under the hero used to be a byte-for-byte table against Blazor. The batteries now
/// take that place, and the front-end section says what Rask is to the frameworks it hosts — a superset,
/// not a rival — which a head-to-head table directly contradicted.
/// </remarks>
public sealed partial class HomePageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_batteries_are_the_first_section_after_the_hero()
    {
        // Qualified: inside a markup host the bare `HomePage` is the chain entry, not the type.
        var id = global::Rask.Site.Pages.HomePage.BatteriesSectionId;
        var sections = Regex.Matches(Render(), "<section[^>]*>");

        Assert.True(sections.Count >= 2, "the landing page renders fewer than two sections.");
        Assert.Contains($"id=\"{id}\"", sections[1].Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("vs Blazor")]
    [InlineData("vs-blazor")]
    [InlineData("than Blazor")]
    public void There_is_no_head_to_head_comparison_with_Blazor(string phrase) =>
        Assert.DoesNotContain(phrase, Render(), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_front_end_section_calls_Rask_a_superset()
    {
        var id = global::Rask.Site.Pages.HomePage.FrontEndsSectionId;
        var html = Render();

        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the landing page renders no #{id} section.");

        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"the #{id} section is never closed.");

        Assert.Contains("superset", html[start..end], StringComparison.Ordinal);
    }

    private static string Render() => RaskTest.Render(() => HomePage).Html;
}
