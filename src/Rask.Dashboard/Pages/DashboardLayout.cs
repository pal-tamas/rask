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
/// The layout inlines the dashboard's whole stylesheet via <see cref="HeadAssets" />. Head-asset
/// contributions are collected from every component in the tree and deduplicated by rendered HTML, so the
/// console is styled correctly inside a host that links no stylesheet of its own, without being emitted
/// twice.
/// </para>
/// <para>
/// The console is served as its own application under <c>/_rask</c> rather than as pages inside the host
/// app, so this document is the console's alone — see <c>RaskMountedApp</c>. Leaving it is a browser
/// navigation rather than a live one, which is the point: an operator polling a queue never shares a
/// render session with an end user's page.
/// </para>
/// </summary>
[Authorize(Policy = RaskDashboardPolicies.Access)]
[Route("_rask")]
public sealed partial class DashboardLayout(
    IEnumerable<IQueuePanel> queues,
    RouteState route,
    DashboardSecurityState security) : Component
{
    /// <summary>
    /// The dashboard's stylesheet, compiled from <c>Styles/dashboard.css</c> by Tailwind at this
    /// package's build and embedded in the assembly.
    /// </summary>
    /// <remarks>
    /// Inlined rather than served. The alternative is a static web asset, which needs the Razor SDK and a
    /// <c>_content/</c> path a host has to map — for a stylesheet this size, a <c>&lt;style&gt;</c> is
    /// smaller than the machinery. It is also why this package ships no assets at all and can be bundled
    /// as a plain assembly.
    /// <para>
    /// Read once into a static: the same bytes on every render, on every request, for the process's life.
    /// </para>
    /// </remarks>
    private static readonly string Css = ReadCss();

    private static string ReadCss()
    {
        var assembly = typeof(DashboardLayout).Assembly;
        using var stream = assembly.GetManifestResourceStream("Rask.Dashboard.dashboard.css");

        // Empty rather than throwing: an unstyled console still shows an operator what is happening, and
        // failing to start a whole application because its dashboard has no CSS would be the worse trade.
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <inheritdoc />
    protected override Component? HeadAssets =>
    [
        Title["Ops"],
        // An operator surface has no business in a search index, even behind a policy.
        Meta.Name("robots").Content("noindex, nofollow"),
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        Style[Raw.Value(Css)],
    ];

    /// <inheritdoc />
    protected override Component? Render() =>
        // .rask-ops is a hook, not a fence. It was a fence while these pages rendered inside the host
        // application's document and every rule had to be scoped under it; the console is now a mounted
        // application with its own document (RaskMountedApp), so the stylesheet is free to be ordinary.
        Div.Class("rask-ops min-h-screen bg-ops-bg text-ops-ink")[
            Header.Class("border-b border-ops-line")[
                Div.Class("mx-auto flex max-w-none items-center gap-6 px-6 py-3")[
                    A.Href(Routes.OverviewPage())
                        .Class("flex items-center gap-2 font-semibold tracking-tight text-ops-ink no-underline")[
                        OpsIcon.Name(OpsIconName.Overview),
                        Span["Ops"]
                    ],
                    Nav.Class("ml-auto flex flex-wrap items-center gap-1")[NavItems()]
                ]
            ],
            Main.Class("mx-auto max-w-none px-6 py-6")[
                UnsecuredWarning(),
                Outlet
            ]
        ];

    private IEnumerable<Component> NavItems()
    {
        yield return NavItem(Routes.OverviewPage(), "Overview", OpsIconName.Overview, exact: true);

        // Only the batteries the app actually registered get a tab, so the nav is an honest inventory of
        // what this deployment runs rather than a menu of mostly-dead links.
        foreach (var queue in queues.Where(q => q.IsAvailable).OrderBy(q => q.Title, StringComparer.Ordinal))
        {
            yield return NavItem(Routes.QueuePage(queue.Slug), queue.Title, queue.Icon, exact: false);
        }

        yield return NavItem(Routes.CachePage(), "Cache", OpsIconName.Archive, exact: false);
        yield return NavItem(Routes.LogsPage(), "Logs", OpsIconName.Queue, exact: false);
        yield return NavItem(Routes.SystemPage(), "System", OpsIconName.Storage, exact: false);
    }

    // Named NavItem, not NavLink: a private method of that name would shadow the NavLink chain entry it
    // needs to call, and the entry is a member of this markup host rather than a type it can qualify.
    private Component NavItem(RouteUrl url, string label, OpsIconName icon, bool exact) =>
        NavLink
            .Key(label)
            .Href(url)
            // ops-nav-link is a TEST contract, carried on both branches so it survives the active
            // state: ShopExampleTests locates the console's nav by it. The utilities beside it are
            // styling and free to change; this one name is not.
            .Class(IsActive(url.Path, exact)
                ? "ops-nav-link flex items-center gap-1.5 rounded-md bg-ops-panel px-3 py-1.5 text-sm font-medium text-ops-ink no-underline"
                : "ops-nav-link flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm text-ops-muted no-underline hover:bg-ops-panel hover:text-ops-ink")[
            OpsIcon.Name(icon).Class("size-4 shrink-0"),
            Span[label]
        ];

    // Exact for the overview, prefix for the rest — otherwise "/_rask" would light up on every page,
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
            ? Div.Role("alert")
                .Class("mb-6 flex items-start gap-3 rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-amber-200")[
                OpsIcon.Name(OpsIconName.ShieldWarning).Class("mt-0.5 size-5 shrink-0"),
                Span[
                    "Unsecured — anyone who can reach this URL can read job payloads, stored emails and logs. Define the ",
                    Code.Class("rounded bg-black/30 px-1 py-0.5 font-mono text-xs")[RaskDashboardPolicies.Access],
                    " authorization policy; without one the dashboard denies everyone outside Development."
                ]
            ]
            : null;
}
