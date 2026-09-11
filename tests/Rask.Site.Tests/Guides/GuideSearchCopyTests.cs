using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

/// <summary>
///     Every guide's search copy fits in a search result and says something no other guide's does.
/// </summary>
/// <remarks>
///     <para>
///         <c>required</c> on <see cref="GuideEntry.SearchTitle" /> and <see cref="GuideEntry.Description" />
///         makes the copy mandatory — a guide added without it does not compile. These make it usable. A
///         title past ~60 characters is cut with an ellipsis in the result; a description past ~160 is cut
///         mid-sentence, and one under ~110 wastes the only two lines a result gets to say what the page is.
///     </para>
///     <para>
///         Asserted per guide rather than in one loop, so a failure names the guide instead of the first
///         of however many are wrong.
///     </para>
/// </remarks>
public sealed class GuideSearchCopyTests
{
    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string>();
        foreach (var guide in GuideCatalog.All)
        {
            data.Add(guide.Slug);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void TheSearchTitleFitsInAResultWithTheSiteName(string slug)
    {
        var guide = GuideCatalog.Find(slug)!;
        var title = guide.SearchTitle + PageMeta.TitleSuffix;

        Assert.True(title.Length <= 60, $"{slug}'s title is {title.Length} characters: \"{title}\"");

        // The suffix already names the site. "Rask" twice in one result reads as padding, and the words
        // before the suffix are the only ones matched against what someone searched for.
        Assert.DoesNotContain("Rask", guide.SearchTitle, StringComparison.Ordinal);
        Assert.Equal(guide.SearchTitle.Trim(), guide.SearchTitle);
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void TheDescriptionFillsAResultWithoutBeingCut(string slug)
    {
        var description = GuideCatalog.Find(slug)!.Description;

        Assert.True(
            description.Length is >= 110 and <= 160,
            $"{slug}'s description is {description.Length} characters (110–160): \"{description}\"");
        Assert.Equal(description.Trim(), description);
    }

    [Fact]
    public void NoTwoGuidesShareATitleOrADescription()
    {
        // Two guides answering to the same words compete with each other for the one result a search engine
        // is willing to show from a site — and a copy-pasted entry is exactly how that happens.
        var titles = GuideCatalog.All.GroupBy(g => g.SearchTitle, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(g => g.Slug)))
            .ToArray();
        var descriptions = GuideCatalog.All.GroupBy(g => g.Description, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(g => g.Slug)))
            .ToArray();

        Assert.True(titles.Length == 0, $"shared search titles: {string.Join("; ", titles)}");
        Assert.True(descriptions.Length == 0, $"shared descriptions: {string.Join("; ", descriptions)}");
    }
}
