using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     An application's whole frame in one line: the menu in Flux's sidebar, breadcrumbs from that menu in the header,
///     who is signed in at the sidebar's foot with a sign-out that asks first, and the page in the main.
/// </summary>
/// <remarks>
///     <code>
///     [Route("/")]
///     public sealed partial class Layout : Component
///     {
///         protected override Component? Render() =>
///             Ui.AppFrame.Brand("PPTA").User(Current.Name).SignOut(SignOut.Path)[
///                 Ui.SidebarNav[
///                     Ui.SidebarItem.Icon(Ui.IconName.Home).Href(Routes.HomePage())["Home"],
///                     Ui.SidebarItem.Icon(Ui.IconName.Truck).Href(Routes.VehiclesPage())["Vehicles"],
///                     Ui.SidebarGroup.Heading("Records").Icon(Ui.IconName.CircleStack)[
///                         Ui.SidebarItem.Icon(Ui.IconName.Identification).Href(Routes.DriversPage())["Drivers"]
///                     ]
///                 ],
///                 Outlet
///             ];
///     }
///     </code>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-04, #1296), laid out of Flux's own parts as its
///     sidebar layout lays them out — <see cref="UiSidebar" /> (sticky, slid over the page below <c>lg</c>) on a
///     <c>zinc-50</c> ground with its edge, <see cref="UiHeader" /> above the page holding the phone's toggle, the
///     breadcrumbs and the <see cref="Notices" />, and <see cref="UiMain" />. It mounts what every page of an app asks in:
///     <see cref="UiToast" />, <see cref="UiConfirmLeave" /> and <see cref="UiConfirm" />, worded by the catalog.
///     </para>
///     <para>
///     Its children are the menu — each <see cref="UiSidebarNav" /> goes to the sidebar, pinned between the brand and the
///     profile while it scrolls — and the page: everything else, the <c>Outlet</c> as a rule, goes to the main, and with it the
///     built-in not-found and forbidden pages. A row is lit while its page is shown or a page beneath it (its form), and
///     the group holding it is opened. A row's own <c>.Current(…)</c> wins. Home is the row for <c>/</c>.
///     </para>
///     <para>
///     The look is the kit's: the accent colour is the app's theme (<c>--color-accent</c> in its stylesheet), and the logo
///     is <see cref="Logo" />.
///     </para>
/// </remarks>
public sealed partial class UiAppFrame : Component
{
    private const string Ground = "border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900";

    private const string Pinned = "min-h-0 flex-1 overflow-y-auto";

    // What the call site said of each row's current and each group's fold, before the frame lit or opened them.
    private readonly Dictionary<Component, bool?> _said = new(ReferenceEqualityComparer.Instance);

    /// <summary>The app's name, at the top of the sidebar, leading home. Required, and taken first.</summary>
    public required string Brand { get; set; }

    /// <summary>The logo's URL, before the name, and all of the brand in a narrowed sidebar.</summary>
    public string? Logo { get; set; }

    /// <summary>The logo shown in dark mode instead.</summary>
    public string? LogoDark { get; set; }

    /// <summary>What the pages are shown inside — a partner, a tenant, a workspace — in a line under the brand.</summary>
    public string? Tenant { get; set; }

    /// <summary>Who is signed in, at the sidebar's foot.</summary>
    public string? User { get; set; }

    /// <summary>
    ///     Where signing out goes — an endpoint that answers a GET. The profile's menu then holds the sign-out row, which
    ///     asks first in the catalog's words (<see cref="UiMenuItem.SignOut" />).
    /// </summary>
    public RouteUrl? SignOut { get; set; }

    /// <summary>What stands at the header's end: how the app's background work is getting on, a count.</summary>
    public Component? Notices { get; set; }

    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        // The address lights the menu and writes the trail, so the frame is drawn again whenever it moves.
        LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();
        var route = UiTableAddress.Route();
        var parts = Flatten(Children).ToList();
        var navs = parts.OfType<UiSidebarNav>().ToList();
        var menu = UiAppFrameMenu.Read(navs, route?.Path ?? "/");
        menu.Light(_said);
        // Pinned between the brand and the profile: the menu scrolls, they stay. Once, though a nav is drawn again.
        foreach (var nav in navs.Where(nav => nav.Class?.Contains(Pinned, StringComparison.Ordinal) != true))
        {
            nav.Class = UiClass.Compose(Pinned, nav.Class);
            BuilderRuntime.MarkChanged(nav);
        }

        return
        [
            Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Mobile).Class("border-e", Ground)[
                Ui.SidebarHeader[
                    Ui.SidebarBrand.Name(Brand).Logo(Logo).LogoDark(LogoDark).Href(menu.Home),
                    Ui.SidebarCollapse
                ],
                Tenant is { Length: > 0 } tenant ? Ui.Text.Class("px-2 sidebar-rail:hidden").Attributes(("data-ui-app-frame-tenant", ""))[tenant] : null,
                navs,
                Profile()
            ],
            Ui.Header.Class("border-b", Ground)[
                Div.Class("lg:hidden")[Ui.SidebarToggle.Inset(Ui.Position.Left)],
                UiAppFrameTrail.Of(menu, route, route is null ? null : UiListAddresses.Of(route)),
                Ui.Spacer,
                Notices
            ],
            Ui.Main[parts.Where(part => part is not UiSidebarNav)],
            Ui.Toast,
            Ui.ConfirmLeave,
            Ui.Confirm
        ];
    }

    // Who is signed in, and the menu that signs them out; nothing when the frame knows neither.
    private Component? Profile()
    {
        if (User is null && SignOut is null)
        {
            return null;
        }

        var profile = Ui.SidebarProfile.Name(User ?? "");
        return SignOut is { } signOut
            ? Ui.Dropdown.Top.Start[
                profile,
                Ui.Menu[Ui.MenuItem.Key("sign-out").Href(signOut).SignOut()]
            ]
            : profile;
    }

    // A collection written among the children is a tagless fragment: a nav inside it is the menu all the same.
    private static IEnumerable<Component> Flatten(IEnumerable<Component?>? children)
    {
        foreach (var child in children ?? [])
        {
            if (child is Fragment fragment)
            {
                foreach (var inner in Flatten(fragment.Children))
                {
                    yield return inner;
                }
            }
            else if (child is not null)
            {
                yield return child;
            }
        }
    }
}
