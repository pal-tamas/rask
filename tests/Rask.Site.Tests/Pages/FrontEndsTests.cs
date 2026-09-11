using System.Text.RegularExpressions;
using Rask.Site.Pages;
using Rask.Testing;

namespace Rask.Site.Tests.Pages;

/// <summary>
/// The landing page's front-end lanes: each one is on the page with a way into its guide, and the
/// heading's count matches how many the section actually shows.
/// </summary>
/// <remarks>
/// <para>
/// The README's equivalent section read "Three front ends" for as long as there were four lanes, and
/// this page had no such section at all. The meta framework lane existed, was documented, and even had
/// a card further down the page — but nothing that framed the choice counted it, and nothing failed,
/// because the number lived in prose alone.
/// </para>
/// <para>
/// So the number is asserted against the markup beside it rather than pinned to a literal: add a lane
/// and the heading has to be reworded, remove one and the same. A pinned <c>4</c> would go stale in
/// exactly the way that produced the wrong number in the first place.
/// </para>
/// </remarks>
public sealed partial class FrontEndsTests : global::Rask.Core.RaskMarkup
{
    /// <summary>Every lane, and the guide its card opens.</summary>
    public static TheoryData<string, string> Lanes =>
        new()
        {
            { "Rask components", "render-modes" },
            { "Islands", "islands" },
            { "TypeScript SPA", "spa" },
            { "Meta framework", "meta" },
        };

    [Theory]
    [MemberData(nameof(Lanes))]
    public void Every_front_end_lane_is_on_the_landing_page(string title, string guide)
    {
        var section = FrontEndsSection();

        Assert.Contains(title, section, StringComparison.Ordinal);
        Assert.Contains($"href=\"/docs/guides/{guide}/\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_heading_counts_the_lanes_the_section_actually_shows()
    {
        var section = FrontEndsSection();

        // One card per lane, and the whole card is the anchor into its guide — so counting the
        // anchors counts the lanes, and keeps counting them when a lane is added.
        var lanes = Regex.Matches(section, "<a ", RegexOptions.None, TimeSpan.FromSeconds(5)).Count;

        // Guard the count itself: a section that stopped rendering cards would otherwise agree with
        // whatever word the heading happened to carry.
        Assert.True(lanes >= 3, $"only {lanes} lane card(s) rendered — the section is not intact.");

        var word = lanes switch
        {
            3 => "Three",
            4 => "Four",
            5 => "Five",
            6 => "Six",
            _ => throw new InvalidOperationException(
                $"the section shows {lanes} lanes and this map has no word for that — extend it, and "
                + "reword the heading to match."),
        };

        Assert.Contains($"{word} front ends", section, StringComparison.Ordinal);
    }

    /// <summary>The rendered front-ends section, sliced out of the landing page by its own id.</summary>
    /// <remarks>
    /// Addressed by the id the page emits rather than by a copy of it, and sliced to the section so a
    /// lane's name appearing elsewhere on a long landing page cannot satisfy an assertion here.
    /// </remarks>
    private static string FrontEndsSection()
    {
        // Qualified: inside a markup host the bare `HomePage` is the chain's Build<HomePage> entry
        // rather than the type, so its static members are not reachable through it.
        var id = global::Rask.Site.Pages.HomePage.FrontEndsSectionId;
        var html = RaskTest.Render(() => HomePage).Html;

        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the landing page renders no #{id} section.");

        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"the #{id} section is never closed.");

        return html[start..end];
    }
}
