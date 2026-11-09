using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What an <see cref="UiAppFrame" /> reads of the menu it is given: the rows that lead to the app's pages, which of them
///     is the page being shown, and home.
/// </summary>
/// <remarks>
///     Only a generated route is the app's page: a string is a link out (an old front end, another site) and is never lit.
///     The row lit is the one whose page the address is, or is beneath — the longest that holds it, so <c>/drivers/12</c>
///     lights Drivers. Home is the row every other row is beneath (<c>/</c> in an app at the root); it holds its own page
///     alone, or it would hold them all.
/// </remarks>
internal sealed class UiAppFrameMenu
{
    private readonly List<UiAppFrameLink> _links;

    private UiAppFrameMenu(List<UiAppFrameLink> links, string path)
    {
        _links = links;
        HomeRow = links.MinBy(link => link.Path.Length) is { } shortest && links.TrueForAll(link => shortest.Holds(link.Path))
            ? shortest
            : null;
        Current = links
            .Where(link => ReferenceEquals(link, HomeRow) ? link.Is(path) : link.Holds(path))
            .MaxBy(link => link.Path.Length);
    }

    /// <summary>The row every other row is beneath: home.</summary>
    internal UiAppFrameLink? HomeRow { get; }

    /// <summary>The row of the page being shown, or of the list it is beneath; none on a page no row leads to.</summary>
    internal UiAppFrameLink? Current { get; }

    /// <summary>Where home is: the menu's home row, so going there stays in the app; the root itself without one.</summary>
    internal RouteUrl Home => HomeRow?.Url ?? "/";

    /// <summary>Whether the page being shown is home, or no row's: its trail is home alone.</summary>
    internal bool AtHome => Current is null || ReferenceEquals(Current, HomeRow);

    /// <summary>Reads the rows of <paramref name="navs" /> against the address <paramref name="path" />.</summary>
    internal static UiAppFrameMenu Read(IEnumerable<UiSidebarNav> navs, string path)
    {
        var links = new List<UiAppFrameLink>();
        foreach (var nav in navs)
        {
            Collect(nav.Children, group: null, links);
        }

        return new UiAppFrameMenu(links, path);
    }

    /// <summary>
    ///     Lights the current row where the call site did not say whether it is current, and opens the group that holds it
    ///     whatever the group said: the reader sees where they are. <paramref name="said" /> keeps what the call site said
    ///     of each row and group before the frame first changed it, so a later render starts from that again.
    /// </summary>
    internal void Light(Dictionary<Component, bool?> said)
    {
        foreach (var link in _links)
        {
            var stated = Said(said, link.Item, link.Item.Current);
            link.Item.Current = stated ?? ReferenceEquals(link, Current);
            BuilderRuntime.MarkChanged(link.Item);
        }

        foreach (var group in _links.Select(link => link.Group).OfType<UiSidebarGroup>().Distinct())
        {
            var stated = Said(said, group, group.Expanded);
            group.Expanded = ReferenceEquals(group, Current?.Group) ? true : stated;
            BuilderRuntime.MarkChanged(group);
        }
    }

    private static bool? Said(Dictionary<Component, bool?> said, Component part, bool? now)
    {
        if (said.TryGetValue(part, out var stated))
        {
            return stated;
        }

        said[part] = now;
        return now;
    }

    // A collection written among the children is a tagless fragment: its rows are the menu's as much as the rest.
    private static void Collect(IEnumerable<Component?>? children, UiSidebarGroup? group, List<UiAppFrameLink> links)
    {
        foreach (var child in children ?? [])
        {
            switch (child)
            {
                case UiSidebarItem { Href: { PageType: not null } url } item:
                    links.Add(new UiAppFrameLink(item, group, url, UiMenuRow.Label(item.Children)));
                    break;
                case UiSidebarGroup inner:
                    Collect(inner.Children, inner, links);
                    break;
                case Fragment fragment:
                    Collect(fragment.Children, group, links);
                    break;
            }
        }
    }
}
