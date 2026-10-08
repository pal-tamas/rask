using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     What the navigation pages' examples stand next to on fluxui.dev and are not themselves: the classes an
///     app's own Tailwind build would emit, and the components of other pages.
/// </summary>
/// <remarks>
///     <para>
///     The kit's sheet holds only what the kit writes, so a class an example hands to <c>Class</c> is spelled
///     out in <see cref="Sheet" /> — unlayered, as an app's later sheet is, so it wins over the kit's own.
///     </para>
///     <para>
///     A dropdown, a tooltip, a table, a card and a select are other pages' components. Each is ONE marked box
///     of the size Flux's takes (<see cref="Skipped" />): <c>parity.mjs</c> holds it to its place and size and
///     leaves what is inside to that component's own page.
///     </para>
/// </remarks>
internal static class NavigationStandIns
{
    public const string Caleb = "https://unavatar.io/x/calebporzio";

    public const string Hugo = "https://unavatar.io/github/hugosaintemarie";

    public const string Josh = "https://unavatar.io/github/joshhanley";

    public const string Bold = "font-weight:700";

    public const string Sheet =
        "<style>.w-64{width:16rem}.w-full{width:100%}.mt-4{margin-top:1rem}.px-2{padding-inline:.5rem}"
        // `mt-2 mb-4` on the separator of the avatar page's card: an app's own utilities.
        + ".parity-rule{margin-top:.5rem;margin-bottom:1rem}"
        + ".only-light:where(.dark,.dark *){display:none}"
        + ".only-dark{display:none}.only-dark:where(.dark,.dark *){display:flex}"
        // Flux's "Launchpad" mark: a cyan-500 disc, white, text-xs, bold.
        + ".logo-launchpad{width:28px;height:28px;border-radius:calc(infinity*1px);background:oklch(71.5% .143 215.221);"
        + "color:#fff;font-size:.75rem;line-height:calc(1/.75);font-weight:700}"
        + ".logo-accent{background:var(--color-fx-accent);color:#fff}.logo-accent:where(.dark,.dark *){color:oklch(27.4% .006 286.033)}"
        + ".logo-tile{display:flex;align-items:center;justify-content:center;flex-shrink:0;width:24px;height:24px;border-radius:.25rem}"
        // Flux's header and sidebar, which the layouts own: zinc-50 on zinc-100, zinc-800 on white/5 on dark.
        + ".stand-in-bar{display:flex;align-items:center;min-height:56px;padding-inline:16px;border-radius:8px;"
        + "background:oklch(98.5% 0 0);border:1px solid oklch(96.7% .001 286.375)}"
        + ".stand-in-bar:where(.dark,.dark *){background:oklch(27.4% .006 286.033);border-color:color-mix(in oklab,#fff 5%,transparent)}"
        + ".stand-in-side{display:flex;flex-direction:column;gap:16px;padding:16px;border-radius:8px;z-index:1;"
        + "background:oklch(98.5% 0 0);border:1px solid oklch(96.7% .001 286.375)}"
        + ".stand-in-side:where(.dark,.dark *){background:oklch(27.4% .006 286.033);border-color:color-mix(in oklab,#fff 5%,transparent)}"
        // `**:ring-zinc-100 dark:**:ring-zinc-800`, Flux's example of a group on another ground.
        + ".ring-ground>*{--tw-ring-color:oklch(96.7% .001 286.375)}"
        + ".ring-ground>*:where(.dark,.dark *){--tw-ring-color:oklch(27.4% .006 286.033)}</style>";

    /// <summary>Another page's component: one marked box, held to its place and size and not looked into.</summary>
    public static Component Skipped(string component, string style, params Component?[] children) =>
        Markup.Div.Style(style).Attributes(("data-ui-" + component, null), ("data-parity-skip", null))[children];
}
