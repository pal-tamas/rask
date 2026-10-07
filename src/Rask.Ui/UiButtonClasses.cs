namespace Rask;

/// <summary>
///     What a <see cref="UiButton" /> looks like, as the Tailwind classes Flux's button computes to.
/// </summary>
/// <remarks>
///     Every string is a whole literal, because Tailwind emits only what it can read whole. Each value was
///     measured on fluxui.dev/components/button in light and in dark (<c>scripts/flux/parity.mjs button</c>
///     holds the kit to it); the few that page does not show are marked where they are written.
/// </remarks>
internal static class UiButtonClasses
{
    /// <summary>What is inside the button, which decides its padding.</summary>
    internal enum Shape
    {
        Text,
        Square,
        IconLeading,
        IconTrailing,
        IconBoth,
    }

    // Disabled is not on Flux's page: dimmed and inert, as the browser already makes it.
    internal const string Base =
        "relative inline-flex items-center font-medium whitespace-nowrap "
        + "disabled:opacity-75 disabled:pointer-events-none data-loading:pointer-events-none";

    // 12px on an icon's side and 16px on the other; nothing at all around a square's glyph. Flux's page shows
    // an icon beside text at base and sm; xs follows the same step down.
    internal static string Size(Ui.ButtonSize size, Shape shape) => (size, shape) switch
    {
        (Ui.ButtonSize.Sm, Shape.Square) => "h-8 w-8 text-sm rounded-md gap-2",
        (Ui.ButtonSize.Sm, Shape.IconLeading) => "h-8 ps-2 pe-3 text-sm rounded-md gap-2",
        (Ui.ButtonSize.Sm, Shape.IconTrailing) => "h-8 ps-3 pe-2 text-sm rounded-md gap-2",
        (Ui.ButtonSize.Sm, Shape.IconBoth) => "h-8 px-2 text-sm rounded-md gap-2",
        (Ui.ButtonSize.Sm, _) => "h-8 px-3 text-sm rounded-md gap-2",
        (Ui.ButtonSize.Xs, Shape.Square) => "h-6 w-6 text-xs rounded-md gap-1",
        (Ui.ButtonSize.Xs, Shape.IconLeading) => "h-6 ps-1.5 pe-2 text-xs rounded-md gap-1",
        (Ui.ButtonSize.Xs, Shape.IconTrailing) => "h-6 ps-2 pe-1.5 text-xs rounded-md gap-1",
        (Ui.ButtonSize.Xs, Shape.IconBoth) => "h-6 px-1.5 text-xs rounded-md gap-1",
        (Ui.ButtonSize.Xs, _) => "h-6 px-2 text-xs rounded-md gap-1",
        (_, Shape.Square) => "h-10 w-10 text-sm rounded-lg gap-2",
        (_, Shape.IconLeading) => "h-10 ps-3 pe-4 text-sm rounded-lg gap-2",
        (_, Shape.IconTrailing) => "h-10 ps-4 pe-3 text-sm rounded-lg gap-2",
        (_, Shape.IconBoth) => "h-10 px-3 text-sm rounded-lg gap-2",
        _ => "h-10 px-4 text-sm rounded-lg gap-2",
    };

    internal static string Align(Ui.Align? align) => align switch
    {
        Ui.Align.Start => "justify-start",
        Ui.Align.End => "justify-end",
        _ => "justify-center",
    };

    internal static string Variant(Ui.ButtonVariant variant, bool hued) => (variant, hued) switch
    {
        (Ui.ButtonVariant.Primary, _) =>
            "bg-fx-accent hover:bg-fx-accent/90 text-fx-accent-foreground border border-black/10 dark:border-0",
        (Ui.ButtonVariant.Danger, _) =>
            "bg-red-500 hover:bg-red-600 dark:bg-red-600 dark:hover:bg-red-500 text-white",
        (Ui.ButtonVariant.Filled, false) =>
            "bg-zinc-800/5 hover:bg-zinc-800/10 dark:bg-white/10 dark:hover:bg-white/20 text-zinc-800 dark:text-white",
        (Ui.ButtonVariant.Filled, true) =>
            "bg-(--ui-hue-400)/20 hover:bg-(--ui-hue-400)/30 dark:bg-(--ui-hue-400)/40 dark:hover:bg-(--ui-hue-400)/50 "
            + "text-(color:--ui-hue-700) dark:text-(color:--ui-hue-200)",
        (Ui.ButtonVariant.Ghost, false) =>
            "bg-transparent hover:bg-zinc-800/5 dark:hover:bg-white/15 text-zinc-800 dark:text-white",
        (Ui.ButtonVariant.Ghost, true) =>
            "bg-transparent hover:bg-(--ui-hue-400)/20 active:bg-(--ui-hue-400)/30 "
            + "dark:hover:bg-(--ui-hue-400)/40 dark:active:bg-(--ui-hue-400)/50 "
            + "text-(color:--ui-hue-700) dark:text-(color:--ui-hue-300)",
        (Ui.ButtonVariant.Subtle, false) =>
            "bg-transparent hover:bg-zinc-800/5 dark:hover:bg-white/15 "
            + "text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-white",
        (Ui.ButtonVariant.Subtle, true) =>
            "bg-transparent hover:bg-(--ui-hue-50) dark:hover:bg-(--ui-hue-950)/72 "
            + "text-[color:color-mix(in_oklab,var(--ui-hue-600)_54%,var(--color-zinc-500))] hover:text-(color:--ui-hue-700) "
            + "dark:text-[color:color-mix(in_oklab,var(--ui-hue-300)_52%,var(--color-zinc-400))] dark:hover:text-(color:--ui-hue-300)",
        (_, false) =>
            "bg-white hover:bg-zinc-50 dark:bg-zinc-700 dark:hover:bg-zinc-600/75 text-zinc-800 dark:text-white "
            + "border border-zinc-200 border-b-zinc-300/80 hover:border-b-zinc-200 "
            + "dark:border-zinc-600 dark:border-b-zinc-600 dark:hover:border-b-zinc-600",
        // The hue is mixed into Flux's own grays: more of it on the bottom edge, more again under the pointer.
        (_, true) =>
            "bg-white hover:bg-[color-mix(in_oklab,var(--ui-hue-400)_6%,var(--color-white))] "
            + "dark:bg-zinc-700 dark:hover:bg-[color-mix(in_oklab,var(--ui-hue-accent)_10%,var(--color-zinc-700))] "
            + "text-(color:--ui-hue-700) dark:text-(color:--ui-hue-accent) border "
            + "border-[color:color-mix(in_oklab,var(--ui-hue-500)_18%,var(--color-zinc-200))] "
            + "border-b-[color:color-mix(in_oklab,color-mix(in_oklab,var(--ui-hue-500)_26%,var(--color-zinc-300))_80%,transparent)] "
            + "hover:border-[color:color-mix(in_oklab,var(--ui-hue-500)_28%,var(--color-zinc-200))] "
            + "hover:border-b-[color:color-mix(in_oklab,var(--ui-hue-500)_36%,var(--color-zinc-200))] "
            + "dark:border-[color:color-mix(in_oklab,var(--ui-hue-accent)_22%,var(--color-zinc-600))] "
            + "dark:border-b-[color:color-mix(in_oklab,var(--ui-hue-accent)_30%,var(--color-zinc-600))] "
            + "dark:hover:border-[color:color-mix(in_oklab,var(--ui-hue-accent)_32%,var(--color-zinc-600))] "
            + "dark:hover:border-b-[color:color-mix(in_oklab,var(--ui-hue-accent)_40%,var(--color-zinc-600))]",
    };

    // The smallest button casts no shadow.
    internal static string Shadow(Ui.ButtonVariant variant, Ui.ButtonSize size) => (variant, size) switch
    {
        (Ui.ButtonVariant.Outline, Ui.ButtonSize.Xs) => "shadow-none",
        (Ui.ButtonVariant.Outline, _) => "shadow-xs",
        (Ui.ButtonVariant.Primary, _) =>
            "shadow-[inset_0px_1px_color-mix(in_oklab,var(--color-white)_20%,transparent)]",
        (Ui.ButtonVariant.Danger, _) =>
            "shadow-[inset_0px_1px_var(--color-red-500),inset_0px_2px_color-mix(in_oklab,var(--color-white)_15%,transparent)] "
            + "dark:shadow-none",
        _ => "",
    };

    // Inside a Ui.ButtonGroup: square where two buttons meet, and one border between them, not two.
    internal static string Grouped(Ui.ButtonSize size) => size switch
    {
        Ui.ButtonSize.Base =>
            "in-data-ui-button-group:rounded-none in-data-ui-button-group:first:rounded-s-lg "
            + "in-data-ui-button-group:last:rounded-e-lg in-data-ui-button-group:not-first:border-s-0",
        _ =>
            "in-data-ui-button-group:rounded-none in-data-ui-button-group:first:rounded-s-md "
            + "in-data-ui-button-group:last:rounded-e-md in-data-ui-button-group:not-first:border-s-0",
    };

    // Measured on Flux: a button inside a tooltip inside a group fuses as the group's own child would, by
    // where the TOOLTIP stands. The button is its tooltip's first child, so the classes above already give it
    // a start corner and a start border; these take both away from every tooltip but the first, and give the
    // last one its end corner.
    internal static string GroupedInTooltip(Ui.ButtonSize size) => size switch
    {
        Ui.ButtonSize.Base =>
            "[[data-ui-button-group]>[data-ui-tooltip]:not(:first-child)>&]:rounded-s-none "
            + "[[data-ui-button-group]>[data-ui-tooltip]:not(:first-child)>&]:border-s-0 "
            + "[[data-ui-button-group]>[data-ui-tooltip]:last-child>&]:rounded-e-lg",
        _ =>
            "[[data-ui-button-group]>[data-ui-tooltip]:not(:first-child)>&]:rounded-s-none "
            + "[[data-ui-button-group]>[data-ui-tooltip]:not(:first-child)>&]:border-s-0 "
            + "[[data-ui-button-group]>[data-ui-tooltip]:last-child>&]:rounded-e-md",
    };

    // Half of what the button is taller than its line: 6px at sm is on Flux's page, the other two follow it.
    internal static string Inset(Ui.Inset inset, Ui.ButtonSize size) => UiClass.Compose(
        inset.HasFlag(Ui.Inset.Top) ? Top(size) : null,
        inset.HasFlag(Ui.Inset.Bottom) ? Bottom(size) : null,
        inset.HasFlag(Ui.Inset.Left) ? Start(size) : null,
        inset.HasFlag(Ui.Inset.Right) ? End(size) : null);

    private static string Top(Ui.ButtonSize size) => size switch
    {
        Ui.ButtonSize.Sm => "-mt-1.5",
        Ui.ButtonSize.Xs => "-mt-1",
        _ => "-mt-2.5",
    };

    private static string Bottom(Ui.ButtonSize size) => size switch
    {
        Ui.ButtonSize.Sm => "-mb-1.5",
        Ui.ButtonSize.Xs => "-mb-1",
        _ => "-mb-2.5",
    };

    private static string Start(Ui.ButtonSize size) => size switch
    {
        Ui.ButtonSize.Sm => "-ms-1.5",
        Ui.ButtonSize.Xs => "-ms-1",
        _ => "-ms-2.5",
    };

    private static string End(Ui.ButtonSize size) => size switch
    {
        Ui.ButtonSize.Sm => "-me-1.5",
        Ui.ButtonSize.Xs => "-me-1",
        _ => "-me-2.5",
    };

    /// <summary>Whether <paramref name="color" /> is one of the seventeen hues and not a gray.</summary>
    internal static bool IsChromatic(Ui.Color color) => color < Ui.Color.Slate;

    // The shades of one hue a coloured button paints with. `accent` is the shade that reads on a dark
    // surface: 400, and 300 for the two hues whose 400 is too dim there.
    internal static string Hue(Ui.Color color) => color switch
    {
        Ui.Color.Red =>
            "[--ui-hue-50:var(--color-red-50)] [--ui-hue-200:var(--color-red-200)] [--ui-hue-300:var(--color-red-300)] [--ui-hue-400:var(--color-red-400)] [--ui-hue-500:var(--color-red-500)] [--ui-hue-600:var(--color-red-600)] [--ui-hue-700:var(--color-red-700)] [--ui-hue-950:var(--color-red-950)] [--ui-hue-accent:var(--color-red-400)]",
        Ui.Color.Orange =>
            "[--ui-hue-50:var(--color-orange-50)] [--ui-hue-200:var(--color-orange-200)] [--ui-hue-300:var(--color-orange-300)] [--ui-hue-400:var(--color-orange-400)] [--ui-hue-500:var(--color-orange-500)] [--ui-hue-600:var(--color-orange-600)] [--ui-hue-700:var(--color-orange-700)] [--ui-hue-950:var(--color-orange-950)] [--ui-hue-accent:var(--color-orange-400)]",
        Ui.Color.Amber =>
            "[--ui-hue-50:var(--color-amber-50)] [--ui-hue-200:var(--color-amber-200)] [--ui-hue-300:var(--color-amber-300)] [--ui-hue-400:var(--color-amber-400)] [--ui-hue-500:var(--color-amber-500)] [--ui-hue-600:var(--color-amber-600)] [--ui-hue-700:var(--color-amber-700)] [--ui-hue-950:var(--color-amber-950)] [--ui-hue-accent:var(--color-amber-400)]",
        Ui.Color.Yellow =>
            "[--ui-hue-50:var(--color-yellow-50)] [--ui-hue-200:var(--color-yellow-200)] [--ui-hue-300:var(--color-yellow-300)] [--ui-hue-400:var(--color-yellow-400)] [--ui-hue-500:var(--color-yellow-500)] [--ui-hue-600:var(--color-yellow-600)] [--ui-hue-700:var(--color-yellow-700)] [--ui-hue-950:var(--color-yellow-950)] [--ui-hue-accent:var(--color-yellow-400)]",
        Ui.Color.Lime =>
            "[--ui-hue-50:var(--color-lime-50)] [--ui-hue-200:var(--color-lime-200)] [--ui-hue-300:var(--color-lime-300)] [--ui-hue-400:var(--color-lime-400)] [--ui-hue-500:var(--color-lime-500)] [--ui-hue-600:var(--color-lime-600)] [--ui-hue-700:var(--color-lime-700)] [--ui-hue-950:var(--color-lime-950)] [--ui-hue-accent:var(--color-lime-400)]",
        Ui.Color.Green =>
            "[--ui-hue-50:var(--color-green-50)] [--ui-hue-200:var(--color-green-200)] [--ui-hue-300:var(--color-green-300)] [--ui-hue-400:var(--color-green-400)] [--ui-hue-500:var(--color-green-500)] [--ui-hue-600:var(--color-green-600)] [--ui-hue-700:var(--color-green-700)] [--ui-hue-950:var(--color-green-950)] [--ui-hue-accent:var(--color-green-400)]",
        Ui.Color.Emerald =>
            "[--ui-hue-50:var(--color-emerald-50)] [--ui-hue-200:var(--color-emerald-200)] [--ui-hue-300:var(--color-emerald-300)] [--ui-hue-400:var(--color-emerald-400)] [--ui-hue-500:var(--color-emerald-500)] [--ui-hue-600:var(--color-emerald-600)] [--ui-hue-700:var(--color-emerald-700)] [--ui-hue-950:var(--color-emerald-950)] [--ui-hue-accent:var(--color-emerald-400)]",
        Ui.Color.Teal =>
            "[--ui-hue-50:var(--color-teal-50)] [--ui-hue-200:var(--color-teal-200)] [--ui-hue-300:var(--color-teal-300)] [--ui-hue-400:var(--color-teal-400)] [--ui-hue-500:var(--color-teal-500)] [--ui-hue-600:var(--color-teal-600)] [--ui-hue-700:var(--color-teal-700)] [--ui-hue-950:var(--color-teal-950)] [--ui-hue-accent:var(--color-teal-400)]",
        Ui.Color.Cyan =>
            "[--ui-hue-50:var(--color-cyan-50)] [--ui-hue-200:var(--color-cyan-200)] [--ui-hue-300:var(--color-cyan-300)] [--ui-hue-400:var(--color-cyan-400)] [--ui-hue-500:var(--color-cyan-500)] [--ui-hue-600:var(--color-cyan-600)] [--ui-hue-700:var(--color-cyan-700)] [--ui-hue-950:var(--color-cyan-950)] [--ui-hue-accent:var(--color-cyan-400)]",
        Ui.Color.Sky =>
            "[--ui-hue-50:var(--color-sky-50)] [--ui-hue-200:var(--color-sky-200)] [--ui-hue-300:var(--color-sky-300)] [--ui-hue-400:var(--color-sky-400)] [--ui-hue-500:var(--color-sky-500)] [--ui-hue-600:var(--color-sky-600)] [--ui-hue-700:var(--color-sky-700)] [--ui-hue-950:var(--color-sky-950)] [--ui-hue-accent:var(--color-sky-400)]",
        Ui.Color.Blue =>
            "[--ui-hue-50:var(--color-blue-50)] [--ui-hue-200:var(--color-blue-200)] [--ui-hue-300:var(--color-blue-300)] [--ui-hue-400:var(--color-blue-400)] [--ui-hue-500:var(--color-blue-500)] [--ui-hue-600:var(--color-blue-600)] [--ui-hue-700:var(--color-blue-700)] [--ui-hue-950:var(--color-blue-950)] [--ui-hue-accent:var(--color-blue-400)]",
        Ui.Color.Indigo =>
            "[--ui-hue-50:var(--color-indigo-50)] [--ui-hue-200:var(--color-indigo-200)] [--ui-hue-300:var(--color-indigo-300)] [--ui-hue-400:var(--color-indigo-400)] [--ui-hue-500:var(--color-indigo-500)] [--ui-hue-600:var(--color-indigo-600)] [--ui-hue-700:var(--color-indigo-700)] [--ui-hue-950:var(--color-indigo-950)] [--ui-hue-accent:var(--color-indigo-300)]",
        Ui.Color.Violet =>
            "[--ui-hue-50:var(--color-violet-50)] [--ui-hue-200:var(--color-violet-200)] [--ui-hue-300:var(--color-violet-300)] [--ui-hue-400:var(--color-violet-400)] [--ui-hue-500:var(--color-violet-500)] [--ui-hue-600:var(--color-violet-600)] [--ui-hue-700:var(--color-violet-700)] [--ui-hue-950:var(--color-violet-950)] [--ui-hue-accent:var(--color-violet-400)]",
        Ui.Color.Purple =>
            "[--ui-hue-50:var(--color-purple-50)] [--ui-hue-200:var(--color-purple-200)] [--ui-hue-300:var(--color-purple-300)] [--ui-hue-400:var(--color-purple-400)] [--ui-hue-500:var(--color-purple-500)] [--ui-hue-600:var(--color-purple-600)] [--ui-hue-700:var(--color-purple-700)] [--ui-hue-950:var(--color-purple-950)] [--ui-hue-accent:var(--color-purple-300)]",
        Ui.Color.Fuchsia =>
            "[--ui-hue-50:var(--color-fuchsia-50)] [--ui-hue-200:var(--color-fuchsia-200)] [--ui-hue-300:var(--color-fuchsia-300)] [--ui-hue-400:var(--color-fuchsia-400)] [--ui-hue-500:var(--color-fuchsia-500)] [--ui-hue-600:var(--color-fuchsia-600)] [--ui-hue-700:var(--color-fuchsia-700)] [--ui-hue-950:var(--color-fuchsia-950)] [--ui-hue-accent:var(--color-fuchsia-400)]",
        Ui.Color.Pink =>
            "[--ui-hue-50:var(--color-pink-50)] [--ui-hue-200:var(--color-pink-200)] [--ui-hue-300:var(--color-pink-300)] [--ui-hue-400:var(--color-pink-400)] [--ui-hue-500:var(--color-pink-500)] [--ui-hue-600:var(--color-pink-600)] [--ui-hue-700:var(--color-pink-700)] [--ui-hue-950:var(--color-pink-950)] [--ui-hue-accent:var(--color-pink-400)]",
        Ui.Color.Rose =>
            "[--ui-hue-50:var(--color-rose-50)] [--ui-hue-200:var(--color-rose-200)] [--ui-hue-300:var(--color-rose-300)] [--ui-hue-400:var(--color-rose-400)] [--ui-hue-500:var(--color-rose-500)] [--ui-hue-600:var(--color-rose-600)] [--ui-hue-700:var(--color-rose-700)] [--ui-hue-950:var(--color-rose-950)] [--ui-hue-accent:var(--color-rose-400)]",
        _ => "",
    };

    // A primary button of a colour is the accent re-pointed for that one button: fill and label, per scheme,
    // as fluxui.dev/themes pairs them. Blue and red are on the button page; the rest follow that table.
    internal static string Accent(Ui.Color color) => color switch
    {
        Ui.Color.Red =>
            "[--color-fx-accent:var(--color-red-500)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Orange =>
            "[--color-fx-accent:var(--color-orange-500)] [--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent:var(--color-orange-400)] dark:[--color-fx-accent-foreground:var(--color-orange-950)]",
        Ui.Color.Amber =>
            "[--color-fx-accent:var(--color-amber-400)] [--color-fx-accent-foreground:var(--color-amber-950)]",
        Ui.Color.Yellow =>
            "[--color-fx-accent:var(--color-yellow-400)] [--color-fx-accent-foreground:var(--color-yellow-950)]",
        Ui.Color.Lime =>
            "[--color-fx-accent:var(--color-lime-400)] [--color-fx-accent-foreground:var(--color-lime-900)] dark:[--color-fx-accent-foreground:var(--color-lime-950)]",
        Ui.Color.Green =>
            "[--color-fx-accent:var(--color-green-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Emerald =>
            "[--color-fx-accent:var(--color-emerald-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Teal =>
            "[--color-fx-accent:var(--color-teal-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Cyan =>
            "[--color-fx-accent:var(--color-cyan-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Sky =>
            "[--color-fx-accent:var(--color-sky-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Blue =>
            "[--color-fx-accent:var(--color-blue-500)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Indigo =>
            "[--color-fx-accent:var(--color-indigo-500)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Violet =>
            "[--color-fx-accent:var(--color-violet-500)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Purple =>
            "[--color-fx-accent:var(--color-purple-500)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Fuchsia =>
            "[--color-fx-accent:var(--color-fuchsia-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Pink =>
            "[--color-fx-accent:var(--color-pink-600)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Rose =>
            "[--color-fx-accent:var(--color-rose-500)] [--color-fx-accent-foreground:var(--color-white)]",
        Ui.Color.Slate =>
            "[--color-fx-accent:var(--color-slate-800)] [--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent:var(--color-white)] dark:[--color-fx-accent-foreground:var(--color-slate-800)]",
        Ui.Color.Gray =>
            "[--color-fx-accent:var(--color-gray-800)] [--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent:var(--color-white)] dark:[--color-fx-accent-foreground:var(--color-gray-800)]",
        Ui.Color.Zinc =>
            "[--color-fx-accent:var(--color-zinc-800)] [--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent:var(--color-white)] dark:[--color-fx-accent-foreground:var(--color-zinc-800)]",
        Ui.Color.Neutral =>
            "[--color-fx-accent:var(--color-neutral-800)] [--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent:var(--color-white)] dark:[--color-fx-accent-foreground:var(--color-neutral-800)]",
        Ui.Color.Stone =>
            "[--color-fx-accent:var(--color-stone-800)] [--color-fx-accent-foreground:var(--color-white)] dark:[--color-fx-accent:var(--color-white)] dark:[--color-fx-accent-foreground:var(--color-stone-800)]",
        _ => "",
    };
}
