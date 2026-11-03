namespace Rask.Site.Features.UiDocs;

/// <summary>The head of a layout's demo page: a title of its own, and its layout's page as the canonical.</summary>
/// <remarks>
///     A demo is that page's example drawn whole, so the page is what a search should find: naming it canonical also
///     keeps the demo out of the sitemap.
/// </remarks>
internal static class UiLayoutDemo
{
    public static Component Meta(string demo, UiDocsEntry page) => Titled(demo + " layout demo", page);

    /// <summary>The head of a demo that is a layout's part rather than a layout: <paramref name="title" /> as it is.</summary>
    public static Component Titled(string title, UiDocsEntry page) =>
        PageMeta.For(title + PageMeta.TitleSuffix, page.Description, page.Path);
}
