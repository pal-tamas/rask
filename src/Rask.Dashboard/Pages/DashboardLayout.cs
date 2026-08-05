using Microsoft.AspNetCore.Authorization;
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
/// The layout contributes its own stylesheet links via <see cref="Head" />. Head contributions are
/// collected from every component in the tree and deduplicated by rendered HTML, so the dashboard is
/// styled correctly inside a host that never linked Bootstrap, without double-linking in one that did —
/// and they drop out again when the operator navigates back into the app.
/// </para>
/// </summary>
[Route("_ops")]
[Authorize(Policy = RaskDashboardPolicies.Access)]
public sealed class DashboardLayout(
    IEnumerable<IQueuePanel> queues,
    RouteState route,
    DashboardSecurityState security) : Component
{
    protected override Component? Head =>
    [
        Title()["Ops"],
        // An operator surface has no business in a search index, even behind a policy.
        Meta(Name: "robots", Content: "noindex, nofollow"),
        BootstrapStyles(),
        RaskTokens(),
    ];

    protected override Component? Render() =>
    [
        BsNavbar(Class: "border-bottom mb-4")[
            BsContainer(Fluid: true)[
                BsNavbarBrand(Href: Routes.OverviewPage(), Class: "d-flex align-items-center gap-2")[
                    BsIcon(Name: BsIconName.Speedometer2),
                    Span()["Ops"]
                ],
                BsNav(Class: "ms-auto")[NavItems()]
            ]
        ],
        BsContainer(Fluid: true)[
            UnsecuredWarning(),
            Outlet()
        ],
    ];

    private IEnumerable<Component> NavItems()
    {
        yield return NavLink(Routes.OverviewPage(), "Overview", BsIconName.Speedometer2, exact: true);

        // Only the batteries the app actually registered get a tab, so the nav is an honest inventory of
        // what this deployment runs rather than a menu of mostly-dead links.
        foreach (var queue in queues.Where(q => q.IsAvailable).OrderBy(q => q.Title, StringComparer.Ordinal))
        {
            yield return NavLink(Routes.QueuePage(queue.Slug), queue.Title, queue.Icon, exact: false);
        }

        yield return NavLink(Routes.CachePage(), "Cache", BsIconName.Archive, exact: false);
        yield return NavLink(Routes.SystemPage(), "System", BsIconName.HddStack, exact: false);
    }

    private Component NavLink(RouteUrl url, string label, BsIconName icon, bool exact) =>
        BsNavItem(Key: label)[
            BsLink(url, Class: Bs.Join(
                "nav-link d-flex align-items-center gap-1",
                IsActive(url.Path, exact) ? "active" : null))[
                BsIcon(Name: icon),
                Span()[label]
            ]
        ];

    // Exact for the overview, prefix for the rest — otherwise "/_ops" would light up on every page,
    // since every dashboard path starts with it.
    private bool IsActive(string href, bool exact)
    {
        var path = route.Path.TrimEnd('/');
        var target = href.TrimEnd('/');
        return exact ? path == target : path.StartsWith(target, StringComparison.Ordinal);
    }

    // The fail-closed default is permissive in Development so `rask dev` just works. That convenience is
    // exactly the thing that gets shipped by accident, so it says so on every page while it applies — and
    // only while it applies: an app that defined the policy has real access control and gets no banner.
    private Component? UnsecuredWarning() =>
        security.IsUnsecured
            ? BsAlert(Color: BsColor.Warning, Class: "d-flex align-items-center gap-2")[
                BsIcon(Name: BsIconName.ExclamationTriangle),
                Span()[
                    "Unsecured — anyone who can reach this URL can read job payloads, stored emails and logs. Define the ",
                    Code()[RaskDashboardPolicies.Access],
                    " authorization policy; without one the dashboard denies everyone outside Development."
                ]
            ]
            : null;
}
