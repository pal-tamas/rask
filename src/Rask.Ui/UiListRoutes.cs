using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     The way back to a list as the reader left it: <c>Routes.SuppliersPage().AsLeft()</c> — its filters, its sort, its
///     page and its search, as this session last showed it.
/// </summary>
/// <remarks>
///     <para>
///     Rask notes every address a session moves to, so no list has to say where it stands and no page has to carry it:
///     a form's save, a "Cancel" of a page of its own, or an old address sending the reader on, writes the list's route
///     and asks for it as left.
///     </para>
///     <code>
///     Routes.SuppliersPage().AsLeft().Go().Replacing();                        // after a save
///     Ui.Button.Ghost.Href(Routes.LandParcelsPage().AsLeft())["Cancel"]        // back to the list
///     Go.To(Routes.DriversPage().AsLeft().With("edit", id)).Replacing();        // an old form address, on to the dialog
///     </code>
///     <para>
///     <see cref="UiFormButtons" />' Cancel, the <see cref="UiAppFrame" />'s breadcrumbs and a <see cref="UiEditorDialog" />
///     closing already go there by themselves. The key of an editor dialog (<c>?edit=</c>) is no part of where a list
///     stands, and is left out. A list the session has not shown is the route as given. Asked as a page renders, the
///     page is drawn again whenever the address moves, so a link written from it stays the way back.
///     </para>
/// </remarks>
public static class UiListRoutes
{
    /// <summary>Where <paramref name="list" /> was last shown in this session, its query kept; the route as given when it was not.</summary>
    /// <param name="list">The list's route: <c>Routes.SuppliersPage()</c>.</param>
    /// <returns>The list's address as the reader left it.</returns>
    public static RouteUrl AsLeft(this RouteUrl list)
    {
        if ((UiTableAddress.Route() ?? Navigator.Current?.Route) is not { } route)
        {
            return list;
        }

        // The way back moves with the address, so what is drawn from it is drawn again whenever it moves.
        LiveRenderContext.CurrentSync?.MarkCurrentReadsAmbientState();
        return UiListAddresses.Of(route).Back(list);
    }
}
