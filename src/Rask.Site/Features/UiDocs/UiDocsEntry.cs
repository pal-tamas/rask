using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>One page of the UI tab: where it lives, its name in the sidebar, and what it tells a search engine.</summary>
/// <param name="Path">The page's generated route.</param>
/// <param name="Title">Its name in the sidebar and its heading.</param>
/// <param name="Group">The sidebar group it sits in.</param>
/// <param name="SearchTitle">Its title in a search result, without the site's suffix: 53 characters at most.</param>
/// <param name="Description">One or two sentences saying what the page is: 110 to 160 characters.</param>
/// <param name="Flux">The slug of the fluxui.dev page it mirrors, for a component page.</param>
internal sealed record UiDocsEntry(
    RouteUrl Path,
    string Title,
    string Group,
    string SearchTitle,
    string Description,
    string? Flux = null)
{
    /// <summary>The fluxui.dev page this one mirrors, or null for a page of Rask's own.</summary>
    public string? FluxUrl => Flux is null ? null : FluxSection + Flux;

    // Flux files its two layouts apart from its components.
    private string FluxSection => string.Equals(Group, UiDocsCatalog.Layouts, StringComparison.Ordinal)
        ? "https://fluxui.dev/layouts/"
        : "https://fluxui.dev/components/";

    /// <summary>The page's head: title, description, canonical, social card and structured data.</summary>
    public Component Meta() => PageMeta.For(SearchTitle + PageMeta.TitleSuffix, Description, Path);
}
