using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The dashboard's shell and the single place access is enforced.
/// <para>
/// <see cref="AuthorizeAttribute" /> sits on the layout, not on each page:
/// <c>RouteAuthorizationGuard</c> evaluates the whole route chain, so one attribute here protects every
/// child — on the initial GET and again on each in-app WebSocket navigation. A page added later is
/// protected by construction rather than by remembering to annotate it.
/// </para>
/// <para>
/// The layout inlines the kit's stylesheet via <see cref="HeadAssets" />, and that is the only stylesheet the
/// console has: every page is drawn with <c>Rask.Ui</c> components, so every class on it is one the kit's
/// compiled sheet already carries. Head-asset contributions are collected from every component in the tree
/// and deduplicated by rendered HTML, so the console is styled correctly inside a host that links no
/// stylesheet of its own, without being emitted twice.
/// </para>
/// <para>
/// The console is served as its own application under <c>/_rask</c> rather than as pages inside the host
/// app, so this document is the console's alone — see <c>RaskMountedApp</c>. Leaving it is a browser
/// navigation rather than a live one, which is the point: an operator polling a queue never shares a
/// render session with an end user's page.
/// </para>
/// <para>
/// The chrome is two rows: a breadcrumb bar saying WHAT you are looking at, and a tab bar saying WHICH
/// PART of the console you are in. That split is why the queues are one tab rather than one tab each — a
/// deployment running jobs, outbox and mail used to spend three of its six top-level tabs on them, which
/// made the nav grow with the batteries instead of describing the console.
/// </para>
/// </summary>
[Authorize(Policy = RaskDashboardPolicies.Access)]
[Route("_rask")]
public sealed partial class DashboardLayout(
    IEnumerable<IQueuePanel> queues,
    RouteState route,
    DashboardSecurityState security) : Component
{
    // Enumerated once and kept: IsAvailable asks whether the battery is registered AND mapped in the EF
    // model, and the chrome asks that question for the sidebar and the switcher on every render.
    private IReadOnlyList<IQueuePanel>? _available;

    // Whether the sidebar is slid over the page, on a phone. Owned here so that going somewhere closes it.
    private bool _navOpen;

    /// <inheritdoc />
    protected override Component? HeadAssets =>
    [
        Title["Ops"],
        // An operator surface has no business in a search index, even behind a policy.
        Meta.Name("robots").Content("noindex, nofollow"),
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        // ONE sheet, the kit's. The console used to compile a second one for the utilities its pages wrote,
        // because Tailwind scans the project it runs in and neither build could see the other's markup; the
        // pages write no classes now, and the document's reset travels in the kit's sheet (UiStylesheet.DocumentAttribute).
        // INLINED here, unlike the apps, and deliberately. The console is mounted into somebody else's host at
        // /_rask: that host references Rask.Dashboard, not Rask.Ui, so it never gets the build target that
        // writes the sheet into wwwroot, and a <link> would point at a file nothing produced.
        Style[Raw.Value(UiStylesheet.Css)],
    ];

    /// <summary>Only the batteries the app actually registered, so the chrome is an honest inventory.</summary>
    private IReadOnlyList<IQueuePanel> Available =>
        _available ??= queues
            .Where(q => q.IsAvailable)
            .OrderBy(q => q.Title, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The path every queue page hangs off, taken from the generated URL rather than spelled again — so
    /// moving the route moves this with it.
    /// </summary>
    private static string QueuesPrefix
    {
        get
        {
            var sample = Routes.QueuePage("x").Path;
            return sample[..sample.LastIndexOf('/')];
        }
    }

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        // Flux's sidebar layout: the sections down the side, a header for what belongs to the page being read,
        // the page in what is left. The three are siblings in the body, which is what makes it the grid.
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Always).Open(_navOpen).OnToggle(open => _navOpen = open)[
            Ui.SidebarHeader[
                // The wordmark and the destination are the console's, not the kit's.
                Ui.SidebarBrand.Name("Ops").Href(Routes.OverviewPage()),
                Ui.SidebarCollapse
            ],
            Ui.SidebarNav[Sections()]
        ],
        Ui.Header[
            Ui.SidebarToggle.Inset(Ui.Position.Left),
            QueueSwitcher(),
            Ui.Spacer,
            Ui.TopLink.Label("Docs").Href("https://rask.sh/docs/")
        ],
        Ui.Main[
            // The landmark: where a screen reader jumps to, and where focus goes after a navigation.
            Main[
                UnsecuredWarning(),
                Outlet
            ]
        ]
    ];

    // ── Chrome ──────────────────────────────────────────────────────────────────────────────────────

    private IEnumerable<Component> Sections()
    {
        yield return Section(Routes.OverviewPage(), "Overview", Ui.IconName.Home, exact: true);

        // One item for every queue. It keeps you on the queue you are already reading and otherwise lands on
        // the first — there is no memory of a previously-viewed queue, and claiming one would be a promise
        // this makes nowhere. A deployment with no queue batteries gets no item at all rather than a dead
        // link.
        if (Available.Count > 0)
        {
            var target = CurrentQueue() ?? Available[0];
            yield return Section(Routes.QueuePage(target.Slug), "Queues", Ui.IconName.QueueList, exact: false, prefix: QueuesPrefix);
        }

        yield return Section(Routes.CachePage(), "Cache", Ui.IconName.CircleStack, exact: false);
        yield return Section(Routes.StoragePage(), "Storage", Ui.IconName.ArchiveBox, exact: false);
        yield return Section(Routes.LogsPage(), "Logs", Ui.IconName.DocumentText, exact: false);
        yield return Section(Routes.SystemPage(), "System", Ui.IconName.ServerStack, exact: false);
    }

    // Stated rather than left to the router: "Queues" is current on every queue's page, whichever one it links to.
    // Pressing one closes the sidebar where it had slid over the page — on a phone it is in the way of what was asked for.
    private Component Section(RouteUrl url, string label, Ui.IconName icon, bool exact, string? prefix = null) =>
        Ui.SidebarItem
            .Href(url)
            .Icon(icon)
            .Tooltip(label)
            .Current(IsActive(prefix ?? url.Path, exact))
            .OnClick(() => _navOpen = false)[label];

    private UiCrumbSwitcher? QueueSwitcher()
    {
        // Only while you are looking at one. Elsewhere the crumb would be asserting a scope the page below
        // it does not actually have.
        if (CurrentQueue() is not { } current)
        {
            return null;
        }

        return Ui.CrumbSwitcher
            .Label("Switch queue")
            .Value(current.Slug)
            .Choices([.. Available.Select(q => (q.Slug, q.Title))])
            .Icon(current.Icon)
            .OnSelect(GoToQueueAsync);
    }

    private Task GoToQueueAsync(string slug)
    {
        if (Available.Any(q => string.Equals(q.Slug, slug, StringComparison.Ordinal)))
        {
            Go.To(Routes.QueuePage(slug).Path);
        }

        return Task.CompletedTask;
    }

    // Matched against the generated URL rather than by parsing the path, so an unknown slug simply selects
    // nothing instead of half-matching.
    //
    // OrdinalIgnoreCase to agree with QueuePage, which resolves its panel case-insensitively (QueuePage
    // .Load). Comparing Ordinal here meant /_rask/queues/Jobs rendered the Jobs queue perfectly while
    // the crumb and the switcher above it silently vanished — the page working and its chrome disagreeing
    // about whether you were on it.
    private IQueuePanel? CurrentQueue()
    {
        var path = route.Path.TrimEnd('/');
        return Available.FirstOrDefault(q =>
            string.Equals(path, Routes.QueuePage(q.Slug).Path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    // Exact for the overview, prefix for the rest — otherwise "/_rask" would light up on every page,
    // since every dashboard path starts with it.
    private bool IsActive(string href, bool exact)
    {
        var path = route.Path.TrimEnd('/');
        var target = href.TrimEnd('/');
        return exact ? string.Equals(path, target, StringComparison.Ordinal) : path.StartsWith(target, StringComparison.Ordinal);
    }

    // The fail-closed default is permissive in Development so `rask dev` just works. That convenience is
    // exactly the thing that gets shipped by accident, so it says so on every page while it applies — and
    // only while it applies: an app that defined the policy has real access control and gets no banner.
    private Component? UnsecuredWarning() =>
        security.IsUnsecured
            ? Ui.Alert.Tone(Ui.Tone.Warning)[
                Ui.Icon.Name(Ui.IconName.ShieldExclamation),
                Span[
                    "Unsecured — anyone who can reach this URL can read job payloads, stored emails and logs. Define the ",
                    Code[RaskDashboardPolicies.Access],
                    " authorization policy; without one the dashboard denies everyone outside Development."
                ]
            ]
            : null;
}
