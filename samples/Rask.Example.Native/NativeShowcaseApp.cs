using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Native.Components.Generated;
using NativeColor = Rask.Native.Components.NativeColor;
using NativeIcon = Rask.Native.Components.NativeIcon;
using AppRoutes = Rask.Example.Shared.Features.Routes;

namespace Rask.Example.Native;

/// <summary>
///     The native head mounts this instead of the shared <see cref="App" /> so the showcase gets a real native
///     header + tab bar on iOS/Android (the shared <c>App</c> can't reference <c>Rask.Native</c>). It composes
///     the native bars as siblings of a <see cref="Rask.Native.Components.NativeWebView" /> that hosts the shared
///     App's page content (<c>base.Render()</c> — the document around it is the framework's, built from this
///     type's inherited <c>Head</c> / <c>BodyClass</c>); the web navbar is dropped under the native shell by the
///     <c>IsNative</c> gate in <c>ShowcaseLayout</c>. No <c>IsNative</c> guard is needed here — this type is only
///     ever mounted by the native heads.
/// </summary>
public sealed class NativeShowcaseApp(RouteState route) : App
{
    // Brand palette — kept in one place and deliberately aligned with the web theme's accent so the native bars
    // and the WebView content read as one app. NativeColor mirrors NativeIcon: one authored value the platform
    // head resolves to a UIColor / Android Color.
    private static readonly NativeColor Brand = NativeColor.Hex("#4C1D95");   // deep violet, matches the site accent
    private static readonly NativeColor OnBrand = NativeColor.White;

    // A contextual segmented filter shown on the Todos page. Selecting a segment re-renders and drives the
    // Todos tab badge — demonstrating a native segmented control and its interplay with a native tab badge.
    private static readonly string[] Filters = ["All", "Active", "Done"];
    private static readonly string?[] Badges = ["2", "1", "1"]; // matches the two seed todos (1 active, 1 done)
    private int _filter;
    private bool _todosRead; // toggled from the overflow menu — hides the Todos badge

    private bool OnTodos => route.Path.StartsWith("/todos", StringComparison.OrdinalIgnoreCase);

    // A guide detail page (/guides/{slug}) is a drill-down from the Guides index, so its header gets a native
    // back button that pops history back to the index (like hardware Back).
    private bool OnGuideDetail => route.Path.StartsWith("/guides/", StringComparison.OrdinalIgnoreCase);

    protected override Component? Render()
    {
        // An overflow menu of secondary actions (shown on every page's header). Selecting an entry re-renders,
        // demonstrating a native pull-down menu (iOS UIMenu / Android PopupMenu) driving native state.
        var overflow = NativeMenuButton(Items:
        [
            NativeMenuItem(Title: "Mark Todos read", Icon: NativeIcon.List, OnClick: () => _todosRead = true),
            NativeMenuItem(Title: "Mark Todos unread", Icon: NativeIcon.Star, OnClick: () => _todosRead = false),
        ]);

        return
        [
            // On Todos, the header shows the segmented filter in place of the title; elsewhere, the brand title
            // (with a back button when on a drill-down guide page). Both carry the overflow menu as a trailing item.
            OnTodos
                ? NativeHeaderBar(
                    Background: Brand, Tint: OnBrand, TitleColor: OnBrand,
                    Segments: Filters, SelectedSegment: _filter, OnSegmentChanged: i => _filter = i,
                    Trailing: [overflow])
                : NativeHeaderBar(Title: "Rask", Background: Brand, Tint: OnBrand, TitleColor: OnBrand,
                    Leading: OnGuideDetail ? NativeBackButton() : null,
                    Trailing: [overflow]),
            NativeWebView()[base.Render()],
            NativeTabBar(
                // Selected tab picks up the brand accent; the rest stay muted (adaptive so dark mode reads well).
                Tint: Brand,
                UnselectedTint: NativeColor.Adaptive(NativeColor.Hex("#6B7280"), NativeColor.Hex("#9CA3AF")),
                Tabs:
                [
                    // Guides is the site root ("/") now that the Welcome landing page is gone.
                    NativeTab(Title: "Guides", Icon: NativeIcon.Home, To: AppRoutes.GuidesIndexPage()),
                    // The badge tracks the segmented filter on Todos, and the overflow menu can clear it.
                    NativeTab(Title: "Todos", Icon: NativeIcon.Custom("checklist", "ic_todo"),
                        To: AppRoutes.TodosPage(), Badge: _todosRead ? null : OnTodos ? Badges[_filter] : "2"),
                ])
            // Selected is omitted — the framework highlights the tab matching the current route.
        ];
    }
}
