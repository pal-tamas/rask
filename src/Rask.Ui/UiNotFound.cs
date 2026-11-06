using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What an address no page answers says, inside the app's frame: a warning callout with one sentence and the way
///     back to the home page — <c>Ui.NotFound</c>.
/// </summary>
/// <remarks>
///     <para>
///     Not a Flux component: an addition the owner approved (2026-11-06), drawn as Flux's <c>flux:callout</c>
///     <c>variant="warning"</c> with its <c>exclamation-circle</c> icon and a small button among its actions. It is what the
///     built-in not-found page draws with the kit on, inside the root layout, so an app needs no not-found page of its own;
///     an app's own <c>[NotFound]</c> page still wins, and can draw this one.
///     </para>
///     <para>
///     The words are the <c>RaskStrings</c> catalog's — <c>PageNotFound</c> and <c>NotFoundBackHome</c> — so a Hungarian
///     reader reads „A kért oldal nem található.” and „Vissza a kezdőlapra”.
///     </para>
/// </remarks>
public sealed partial class UiNotFound : Component
{
    /// <summary>Where the button leads: <c>.Home(Routes.DashboardPage())</c>. The root, <c>/</c>, when unset.</summary>
    public RouteUrl? Home { get; set; }

    /// <summary>Classes for the call site, added to the callout's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Callout.Warning.Icon(Ui.IconName.ExclamationCircle).Class(Class)
            .Heading(RaskStrings.Get(RaskString.PageNotFound, "The page you requested could not be found."))
            .Actions(Ui.Button.Sm.Key("home").Href(Home ?? "/")[
                RaskStrings.Get(RaskString.NotFoundBackHome, "Back to home")]);
}
