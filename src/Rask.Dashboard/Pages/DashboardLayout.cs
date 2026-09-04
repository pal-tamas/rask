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
    Navigator navigator,
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

    // Enumerated once and kept: IsAvailable asks whether the battery is registered AND mapped in the EF
    // model, and the chrome asks that question for the tab bar, the crumb and the switcher on every render.
    private IReadOnlyList<IQueuePanel>? _available;

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
        // The KIT's sheet first, then the console's own. Two sheets rather than one because Tailwind
        // scans the project it runs in, so the classes Rask.Ui's components write are compiled there and
        // the classes these pages write are compiled here — neither build can see the other's markup.
        // Order is the contract: the console's @theme redefines the --color-ui-* tokens the kit declares,
        // and an override only wins while it is the copy the cascade reads last.
        Style[Raw.Value(UiStylesheet.Css)],
        Style[Raw.Value(Css)],
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
    private string QueuesPrefix
    {
        get
        {
            var sample = Routes.QueuePage("x").Path;
            return sample[..sample.LastIndexOf('/')];
        }
    }

    /// <inheritdoc />
    protected override Component? Render() =>
        UiShell[
            UiTopBar.Trailing(UiTopLink.Label("Docs").Href("https://rask.sh/docs/"))[
                // The wordmark and the destination are the console's, not the kit's — the kit is shared
                // with the site and the docs now, and each says its own name.
                UiBrand.Label("Ops").Href(Routes.OverviewPage()),
                QueueSeparator(),
                QueueSwitcher()
            ],
            UiNav[NavTabs()],
            UiMain[
                UnsecuredWarning(),
                Outlet
            ]
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
        yield return Tab(Routes.LogsPage(), "Logs", exact: false);
        yield return Tab(Routes.SystemPage(), "System", exact: false);
    }

    // Named Tab, not NavTab: a private method named after a chain entry would shadow the entry it needs to
    // call, and the entry is a member of this markup host rather than a type it can qualify.
    private Component Tab(RouteUrl url, string label, bool exact, string? prefix = null) =>
        UiNavTab
            .Label(label)
            .Href(url)
            .Active(IsActive(prefix ?? url.Path, exact));

    private Component? QueueSeparator() =>
        CurrentQueue() is null ? null : UiCrumbSeparator;

    private Component? QueueSwitcher()
    {
        // Only while you are looking at one. Elsewhere the crumb would be asserting a scope the page below
        // it does not actually have.
        if (CurrentQueue() is not { } current)
        {
            return null;
        }

        return UiCrumbSwitcher
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
            navigator.NavigateTo(Routes.QueuePage(slug).Path);
        }

        return Task.CompletedTask;
    }

    // Matched against the generated URL rather than by parsing the path, so an unknown slug simply selects
    // nothing instead of half-matching.
    //
    // OrdinalIgnoreCase to agree with QueuePage, which resolves its panel case-insensitively (QueuePage
    // .LoadAsync). Comparing Ordinal here meant /_rask/queues/Jobs rendered the Jobs queue perfectly while
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
        return exact ? path == target : path.StartsWith(target, StringComparison.Ordinal);
    }

    // The fail-closed default is permissive in Development so `rask dev` just works. That convenience is
    // exactly the thing that gets shipped by accident, so it says so on every page while it applies — and
    // only while it applies: an app that defined the policy has real access control and gets no banner.
    private Component? UnsecuredWarning() =>
        security.IsUnsecured
            ? Div.Role("alert")
                .Class(
                    "mb-4 flex items-start gap-3 rounded-xl border border-ui-warn/40 bg-ui-warn/10 px-4 py-3 "
                    + "text-sm text-ui-ink sm:mb-6")[
                UiIcon.Name(UiIconName.ShieldWarning).Class("mt-0.5 size-5 shrink-0 text-ui-warn-ink"),
                Span.Class("min-w-0 break-words")[
                    "Unsecured — anyone who can reach this URL can read job payloads, stored emails and logs. Define the ",
                    Code.Class("rounded bg-ui-warn/15 px-1 py-0.5 font-mono text-xs")[RaskDashboardPolicies.Access],
                    " authorization policy; without one the dashboard denies everyone outside Development."
                ]
            ]
            : null;
}
