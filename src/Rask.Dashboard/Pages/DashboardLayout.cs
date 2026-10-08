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
    // model, and the chrome asks that question for the tab bar, the crumb and the switcher on every render.
    private IReadOnlyList<IQueuePanel>? _available;

    /// <inheritdoc />
    protected override Component? HeadAssets =>
    [
        Title["Ops"],
        // An operator surface has no business in a search index, even behind a policy.
        Meta.Name("robots").Content("noindex, nofollow"),
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        // ONE sheet: the console's own, with the kit compiled into it (Styles/dashboard.css) — so a
        // utility a page here writes and a class a kit component writes are ranked by Tailwind, in one
        // build. The frame's reset travels with the kit, keyed to Ui.Shell.
        // INLINED here, unlike the apps, and deliberately. The console is mounted into somebody else's host at
        // /_rask, and a <link> would point at a file nothing in that host's wwwroot produced.
        Style[Raw.Value(DashboardStylesheet.Css)],
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
        // The shell carries the kit's theme scope, so it is also where daisyUI reads data-theme. Named
        // rather than left to default: the default is "follow prefers-color-scheme", which would repaint this
        // subtree dark. RaskDashboardShell pins the same theme on <html>; DashboardTheme is the one place the
        // two agree.
        Ui.Shell.Theme(DashboardTheme.Name)[
            Ui.TopBar.Trailing(Ui.Navbar[Ui.NavbarItem.Href("https://rask.sh/docs/")["Docs"]])[
                // The wordmark and the destination are the console's, not the kit's — the kit is shared
                // with the site and the docs now, and each says its own name.
                Ui.Brand.Name("Ops").Logo(Ui.Icon.Name(Ui.IconName.Squares2x2).Mini).Href(Routes.OverviewPage()),
                QueueCrumbs()
            ],
            Ui.Navbar[NavTabs()],
            Ui.Main[
                UnsecuredWarning(),
                Outlet
            ],
            // Where a page's Toast.Success("Evicted …") shows: the console is a mounted app with its own
            // document, so the host places none for it.
            Ui.Toast
        ];

    // ── Chrome ──────────────────────────────────────────────────────────────────────────────────────

    private IEnumerable<Component> NavTabs()
    {
        yield return Tab(Routes.OverviewPage(), "Overview", exact: true);

        // One tab for every queue. It keeps you on the queue you are already reading and otherwise lands on
        // the first — there is no memory of a previously-viewed queue, and claiming one would be a promise
        // this makes nowhere. A deployment with no queue batteries gets no tab at all rather than a dead
        // link.
        if (Available.Count > 0)
        {
            var target = CurrentQueue() ?? Available[0];
            yield return Tab(Routes.QueuePage(target.Slug), "Queues", exact: false, prefix: QueuesPrefix);
        }

        yield return Tab(Routes.CachePage(), "Cache", exact: false);
        yield return Tab(Routes.StoragePage(), "Storage", exact: false);
        yield return Tab(Routes.LogsPage(), "Logs", exact: false);
        yield return Tab(Routes.SystemPage(), "System", exact: false);
    }

    // Named Tab, not NavbarItem: a private method named after a chain entry would shadow the entry it needs
    // to call, and the entry is a member of this markup host rather than a type it can qualify.
    private Component Tab(RouteUrl url, string label, bool exact, string? prefix = null) =>
        Ui.NavbarItem
            .Href(url)
            .Current(IsActive(prefix ?? url.Path, exact))[label];

    private Component? QueueCrumbs()
    {
        // Only while you are looking at one. Elsewhere the crumb would be asserting a scope the page below
        // it does not actually have.
        if (CurrentQueue() is not { } current)
        {
            return null;
        }

        // Flux's breadcrumb with a dropdown in it: the trail says where you are, and its last step is the
        // way to the queues beside this one. Links, so switching is a navigation and nothing round-trips.
        return Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Separator(Ui.IconName.Slash)["Queues"],
            Ui.BreadcrumbsItem[
                Ui.Dropdown[
                    Ui.Button.Ghost.Sm.Icon(current.Icon).IconTrailing(Ui.IconName.ChevronUpDown)[current.Title],
                    Ui.Navmenu[
                        Available.Select(queue => Ui.NavmenuItem
                            .Href(Routes.QueuePage(queue.Slug))
                            .Key(queue.Slug)
                            .Icon(queue.Icon)[queue.Title])
                    ]
                ]
            ]
        ];
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
            ? Ui.Callout.Warning.Icon(Ui.IconName.ShieldExclamation)[
                Ui.CalloutHeading["Unsecured — anyone who can reach this URL can read job payloads, stored emails and logs."],
                Ui.CalloutText[
                    "Define the ",
                    Code[RaskDashboardPolicies.Access],
                    " authorization policy; without one the dashboard denies everyone outside Development."
                ]
            ]
            : null;
}
