namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     What the navigation pages' examples share on fluxui.dev and are not themselves: the people they show and
///     the classes an app's own Tailwind build would emit.
/// </summary>
/// <remarks>
///     <para>
///     The kit's sheet holds only what the kit writes, so a class an example hands to <c>Class</c> is spelled
///     out in <see cref="Sheet" /> — unlayered, as an app's later sheet is, so it wins over the kit's own.
///     </para>
///     <para>
///     Another page's component beside them is the real <c>Ui.*</c> one: a header, a sidebar, a dropdown and
///     what it opens, a table, a select.
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
        + ".logo-accent{background:var(--color-accent);color:#fff}.logo-accent:where(.dark,.dark *){color:oklch(27.4% .006 286.033)}"
        + ".logo-tile{display:flex;align-items:center;justify-content:center;flex-shrink:0;width:24px;height:24px;border-radius:.25rem}"
        // `px-4! w-full bg-zinc-50 dark:bg-zinc-800 rounded-lg border border-zinc-100 dark:border-white/5`, what the
        // examples hand Flux's header: zinc-50 on zinc-100, zinc-800 on white/5 on dark.
        + ".parity-bar{padding-inline:1rem!important;width:100%;border-radius:.5rem;"
        + "background:oklch(98.5% 0 0);border:1px solid oklch(96.7% .001 286.375)}"
        + ".parity-bar:where(.dark,.dark *){background:oklch(27.4% .006 286.033);border-color:color-mix(in oklab,#fff 5%,transparent)}"
        // `w-full bg-zinc-50 dark:bg-zinc-800 border rounded-lg border-zinc-100 dark:border-white/5`, the sidebar's.
        + ".parity-side{width:100%;background:oklch(98.5% 0 0);border:1px solid oklch(96.7% .001 286.375);border-radius:.5rem}"
        + ".parity-side:where(.dark,.dark *){background:oklch(27.4% .006 286.033);border-color:color-mix(in oklab,#fff 5%,transparent)}"
        // `**:ring-zinc-100 dark:**:ring-zinc-800`, Flux's example of a group on another ground.
        + ".ring-ground>*{--tw-ring-color:oklch(96.7% .001 286.375)}"
        + ".ring-ground>*:where(.dark,.dark *){--tw-ring-color:oklch(27.4% .006 286.033)}</style>";
}
