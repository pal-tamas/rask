using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     The breadcrumbs an <see cref="UiAppFrame" /> writes from its menu: home, the group, the list — and beneath a list,
///     the page's own title.
/// </summary>
/// <remarks>
///     <para>
///     On a list: home › group › the list's title, the last one plain. On a page beneath it (its form): home › group ›
///     the list, a link back to it as it was left — its filters, sort and page kept (<see cref="UiListAddresses" />) — ›
///     the page's title, word for word. A group whose one row bears its own name is not said twice; a page no row leads to,
///     and home itself, is home alone.
///     </para>
///     <para>A phone shows the last two steps: the ones before them hide beneath the <c>sm</c> breakpoint.</para>
/// </remarks>
internal static class UiAppFrameTrail
{
    private const string WideOnly = "max-sm:hidden";

    /// <summary>The trail to the page at <paramref name="route" />.</summary>
    internal static Component Of(UiAppFrameMenu menu, RouteState? route, UiListAddresses? addresses)
    {
        List<Step> steps = [new("home", menu.Home, null)];
        if (!menu.AtHome && menu.Current is { } list && route is not null)
        {
            var beneath = route.Path.TrimEnd('/').Length > list.Path.Length;
            if (list.Group?.Heading is { } heading && !string.Equals(heading, list.Label, StringComparison.Ordinal))
            {
                steps.Add(new("group", null, heading));
            }

            if (!beneath)
            {
                steps.Add(new("list", null, route.Title ?? list.Label));
            }
            else
            {
                // Up to the list as it was left: its filters, its sort and its page kept.
                steps.Add(new("back", addresses?.Back(list.Url) ?? list.Url, list.Label));
                if (route.Title is { } title)
                {
                    steps.Add(new("page", null, title));
                }
            }
        }

        return Ui.Breadcrumbs[steps.Select((step, at) => Item(step, wideOnly: at < steps.Count - 2))];
    }

    private static Component Item(Step step, bool wideOnly)
    {
        var item = Ui.BreadcrumbsItem.Key(step.Key);
        if (wideOnly)
        {
            item.Class(WideOnly);
        }

        if (step.Href is { } href)
        {
            item.Href(href);
        }

        return step.Words is { } words ? item[words] : item.Icon(Ui.IconName.Home);
    }

    // One step: a link where it leads somewhere, its words, or home's icon where it has none.
    private sealed record Step(string Key, RouteUrl? Href, string? Words);
}
