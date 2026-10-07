using Rask.Core;

namespace Rask.UiTests.Flux;

/// <summary>
///     One layout page's demos: each a whole document, as Flux UI's docs show a layout.
/// </summary>
/// <remarks>
///     <para>
///     A layout cannot sit in a preview box, so Flux's page links to a full-screen demo under each heading
///     (<c>fluxui.dev/demo/sidebar-inset</c>). <see cref="FluxParityPages" /> writes one document per demo and
///     <c>scripts/flux/parity.mjs layouts/sidebar</c> measures both sides whole: at desktop and phone widths,
///     in light and dark, with the sidebar as it loads, narrowed to its rail and slid over the page.
///     </para>
///     <para>
///     A demo is named by the id of the <c>&lt;h2&gt;</c> it is under, and the first by the page's slug. Other
///     components a demo places in the layout — a navbar, a brand, a heading — are compared as boxes only, so
///     a box of the measured size (<c>slot-*</c> in <see cref="AppUtilities" />) stands where each goes until it is built.
///     </para>
/// </remarks>
public abstract partial class FluxLayoutParity : global::Rask.Core.RaskMarkup
{
    /// <summary>The page's slug on fluxui.dev: <c>sidebar</c> for <c>/layouts/sidebar</c>.</summary>
    public abstract string Page { get; }

    /// <summary>What an app's own Tailwind build emits for the classes these demos hand to <c>Class</c>.</summary>
    public abstract string AppUtilities { get; }

    public abstract IEnumerable<(string Demo, string BodyClass, Component Body)> Demos();

    /// <summary>A demo's body: the layout's parts, side by side in the document.</summary>
    protected static Component Document(Component parts) => parts;
}
