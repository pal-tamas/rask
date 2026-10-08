using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Site;

// Rooted at /docs, not /, and that is what makes one app out of what used to be two.
//
// The landing page and the showcase were separate WASM apps, published to / and /docs/ and stitched
// together by the Pages workflow. Merged into one app they both wanted /, which is a real collision
// (RASK031) rather than a technicality: a page at a layout's own prefix leaves which one renders
// arbitrary. Rooting the layout where the showcase was already published resolves it and costs
// nothing — every rask.sh/docs/... URL, every guide link and every reference in the docs still
// resolves to the same page it did before the merge.
[Route("/docs")]
public sealed partial class ShowcaseLayout(RouteState route, IEnumerable<ShowcaseNavEntry> extraNav)
    : Component
{
    // MatchPrefix: optional section prefix for parameterised links. When set, the
    // sidebar entry stays highlighted for any URL under that prefix (e.g. switching
    // /realtime/BTC ↔ /realtime/ETH keeps "Live ticker" active). Null means
    // exact-match only.
    private static readonly (RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)[] Links =
    [
        // Paths are type-safe, generator-emitted route URLs (Routes.*) — RouteUrl converts
        // implicitly to the string Path slot, so a renamed/removed [Route] is a compile error here, not a
        // dead link. MatchPrefix stays a bare string (it is a URL prefix, not a whole route).
        (Routes.TodosPage(), "Todos", Ui.IconName.CheckCircle, "Apps", null)
        // Many example pages are now folded into their guides as inline live demos: HttpClient+DI /
        // upload / download → HTTP & files (docs/http-and-files.md); typed browser-API wrappers → Browser
        // APIs (docs/browser-apis.md); Events + Toast messages → Composition (docs/composition.md); the
        // User & auth → Authentication (docs/authentication.md); User components → Getting started
        // (docs/getting-started.md); Live ticker → Lifecycle (docs/lifecycle.md); Master-detail →
        // Composition (docs/composition.md keyed lists). See DemoRegistry.
    ];

    // The search filter text, and the set of expanded sidebar groups (keyed by section + group): plain
    // component fields. Whether the sidebar is slid over the page is the sidebar's own checkbox.
    private string _filter = "";
    private readonly HashSet<string> _openGroups = new(StringComparer.Ordinal);

    // Subscribe to RouteState.Changed so the sidebar's active-class computation refreshes on every
    // nav (including browser back/forward), the mobile drawer closes after navigating, and the group
    // holding the active route auto-expands. NavLink does its own active styling; this only drives the
    // drawer/expand side effects and the layout re-render.
    protected override async Task OnMount()
    {
        route.Changed += OnRouteChanged;
        OpenGuideGroups();
        OpenActiveGroup();
    }

    protected override async Task OnUnmount() => route.Changed -= OnRouteChanged;

    private void OnRouteChanged(object? sender, EventArgs e)
    {
        OpenActiveGroup();
        StateHasChanged();
    }

    protected override Component? Render() =>
    [
        // THE LANDING PAGE'S BAR, not one shaped like it: a visitor crossing from `/` to `/docs` keeps the
        // same header. The hamburger is the only thing the docs add, and it has to be in the bar because that
        // is where a thumb reaches for it. It is the kit's sidebar toggle — a label for the sidebar's checkbox,
        // so the sidebar opens on a prerendered page with no runtime — and it hides itself where the sidebar docks.
        SiteHeader
            .FullBleed(true)
            .Leading(Ui.SidebarToggle
                .Icon(Ui.IconName.Bars3)
                .Class("hamburger-btn size-11")),
        // Flux's sidebar layout, under the site's own bar: whatever holds Ui.Main is the grid, so the two are
        // wrapped — the bar is above the grid, not a row of it. Docked from md up and sliding over the page
        // below it; the open state is the sidebar's checkbox, which the runtime unchecks when a navigation completes.
        // The docked sidebar sits under the sticky bar rather than under the viewport's top edge, and clears
        // the bar and the notch while it slides over the page. `!` where the kit sets the same property.
        Div.Class("app-shell")[
            Ui.Sidebar
                .Sticky(true)
                .Collapsible(Ui.SidebarCollapsible.Mobile)
                .Breakpoint(Ui.Breakpoint.Md)
                .Class("side-nav w-72! gap-2! overflow-hidden! border-e border-ui-line bg-ui-bg px-3! "
                       + "pt-[calc(var(--nav-h)+env(safe-area-inset-top))]! md:top-(--nav-h)! "
                       + "md:h-[calc(100dvh-var(--nav-h))] md:max-h-[calc(100dvh-var(--nav-h))]! md:w-[280px]! md:pt-4!")[
                // The filter holds its place and the list below it scrolls: the sidebar itself does not.
                Ui.SidebarSearch
                    .Value(_filter)
                    .Placeholder("Filter guides & examples…")
                    .OnInput(v => _filter = v ?? "")
                    .Class("side-nav-search shrink-0"),
                Ui.SidebarNav
                    .Class("side-nav-scroll min-h-0 flex-1 overflow-y-auto overscroll-contain")[
                    BuildSections()
                ]
            ],
            Ui.Main.Class("min-w-0 px-3! py-4! pb-[calc(2rem_+_env(safe-area-inset-bottom))]! md:px-5!")[
                // The landmark: where a screen reader jumps to, and where focus goes after a navigation.
                Main.Class("page-main mx-auto max-w-[1280px] page-main-inner")[Outlet]
            ]
        ],
        // Once, for every page under this layout: where a form's ConfirmLeave asks, in place of `confirm`.
        Ui.ConfirmLeave
    ];

    // Guides-first: the narrative guides are the primary spine (top of the sidebar, groups expanded by
    // default via OpenGuideGroups), followed by the interactive Examples (the framework/core showcase
    // plus any host-contributed entries, e.g. the WASM PWA examples) and the Bootstrap-component
    // showcase — both demoted below the guides and collapsed until visited.
    private IEnumerable<(string Section, IEnumerable<(RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> Links)> Sections()
    {
        yield return ("Guides", GuidesNav());
        yield return ("Examples",
            Links.Concat(extraNav.Select(e => (e.Path, e.Label, e.Icon, e.Group, e.MatchPrefix))));
    }

    // The Guides section mirrors the GuideCatalog (docs/*.md rendered on-site), led by the index.
    private static IEnumerable<(RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> GuidesNav()
    {
        yield return (Routes.GuidesIndexPage(), "All guides", Ui.IconName.BookOpen, "Overview", null);
        foreach (var g in Features.GuideCatalog.All)
        {
            yield return (Routes.GuidePage(g.Slug), g.Title, g.Icon, g.Group, null);
        }
    }

    private List<Component> BuildSections()
    {
        var children = new List<Component>();
        var filtering = _filter.Length > 0;

        foreach (var (section, links) in Sections())
        {
            var groups = new List<Component>();
            foreach (var (group, items) in GroupConsecutive(links))
            {
                var visible = filtering
                    ? items.Where(i => i.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList()
                    : items;
                if (visible.Count == 0)
                {
                    continue;
                }

                var key = GroupKey(section, group);
                var open = filtering || _openGroups.Contains(key);
                groups.Add(GroupBlock(key, group, open, visible));
            }

            if (groups.Count == 0)
            {
                continue;
            }

            children.Add(Ui.SidebarGroup.Key(section).Heading(section).Class("side-nav-section")[groups]);
        }

        if (children.Count == 0)
        {
            children.Add(Div.Class("side-nav-empty px-3 py-4 text-sm text-zinc-500")["Nothing matches that filter."]);
        }

        return children;
    }

    // One expandable group of the kit's sidebar: a native <details>, so it folds with no round trip, and its
    // state is mirrored here so the group holding the page being read can be opened from C#.
    private Component GroupBlock(
        string key, string group, bool open,
        IReadOnlyList<(RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> items) =>
        Ui.SidebarGroup
            .Key(key)
            .Expandable(true)
            .Heading(group)
            .Expanded(open)
            .OnToggle(expanded => SetGroup(key, expanded))
            .Class("nav-group")[
            items.Select(i =>
            {
                var item = Ui.SidebarItem
                    .Key(i.Path.ToString())
                    .Href(PageMeta.LinkTo(i.Path))
                    .Icon(i.Icon)
                    .Class("side-nav-link");

                // A section's entry stays current on every page under its prefix, which the router cannot know.
                if (i.MatchPrefix is { } prefix)
                {
                    item = item.Current(IsActive(i.Path, prefix));
                }

                return item[i.Label];
            })
        ];

    // While a filter is typed every group is shown open, and that is not the reader's choice to remember.
    private void SetGroup(string key, bool open)
    {
        if (_filter.Length > 0)
        {
            return;
        }

        if (open)
        {
            _openGroups.Add(key);
        }
        else
        {
            _openGroups.Remove(key);
        }
    }

    private void OpenGuideGroups()
    {
        foreach (var (group, _) in GroupConsecutive(GuidesNav()))
        {
            _openGroups.Add(GroupKey("Guides", group));
        }
    }

    // Expands the group containing the active route so a deep link / back-forward lands with its
    // section open. Leaves any groups the user opened by hand untouched (this only adds).
    private void OpenActiveGroup()
    {
        foreach (var (section, links) in Sections())
        {
            if (links.FirstOrDefault(link => IsActive(link.Path, link.MatchPrefix)) is { Path.Path: not null } active)
            {
                _openGroups.Add(GroupKey(section, active.Group));
                return;
            }
        }
    }

    private static string GroupKey(string section, string group) => $"{section}\u001f{group}";

    // Groups consecutive links by their Group label, preserving the array order (the sidebar shows
    // groups in the order their first item appears, exactly as the flat list was authored).
    private static IEnumerable<(string Group, List<(RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> Items)>
        GroupConsecutive(IEnumerable<(RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)> links)
    {
        string? current = null;
        List<(RouteUrl Path, string Label, Ui.IconName Icon, string Group, string? MatchPrefix)>? bucket = null;

        foreach (var link in links)
        {
            if (!string.Equals(link.Group, current, StringComparison.Ordinal))
            {
                if (bucket is not null)
                {
                    yield return (current!, bucket);
                }

                current = link.Group;
                bucket = [];
            }

            bucket!.Add(link);
        }

        if (bucket is not null)
        {
            yield return (current!, bucket);
        }
    }

    private bool IsActive(string href, string? matchPrefix = null)
    {
        if (href is "/")
        {
            return route.Path is "/" || string.IsNullOrEmpty(route.Path);
        }

        var trimmed = route.Path.TrimEnd('/');
        if (string.Equals(trimmed, href, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (matchPrefix is null)
        {
            return false;
        }

        var trimmedPrefix = matchPrefix.TrimEnd('/');
        return string.Equals(trimmed, trimmedPrefix, StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith(trimmedPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
