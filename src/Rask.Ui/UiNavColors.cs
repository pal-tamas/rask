namespace Rask;

/// <summary>
///     What each <see cref="Ui.Color" /> is drawn as in the navigation pieces — Tailwind's default palette in the
///     shades measured on Flux's pages, each a whole class so Tailwind reads it.
/// </summary>
internal static class UiNavColors
{
    /// <summary>The tinted count on a nav bar or navlist item: Flux's badge colours.</summary>
    internal static string Badge(Ui.Color color) => color switch
    {
        Ui.Color.Red => "text-red-700 bg-red-400/20 dark:text-red-200 dark:bg-red-400/40",
        Ui.Color.Orange => "text-orange-700 bg-orange-400/20 dark:text-orange-200 dark:bg-orange-400/40",
        Ui.Color.Amber => "text-amber-700 bg-amber-400/25 dark:text-amber-200 dark:bg-amber-400/40",
        Ui.Color.Yellow => "text-yellow-800 bg-yellow-400/25 dark:text-yellow-200 dark:bg-yellow-400/40",
        Ui.Color.Lime => "text-lime-800 bg-lime-400/25 dark:text-lime-200 dark:bg-lime-400/40",
        Ui.Color.Green => "text-green-800 bg-green-400/20 dark:text-green-200 dark:bg-green-400/40",
        Ui.Color.Emerald => "text-emerald-800 bg-emerald-400/20 dark:text-emerald-200 dark:bg-emerald-400/40",
        Ui.Color.Teal => "text-teal-800 bg-teal-400/20 dark:text-teal-200 dark:bg-teal-400/40",
        Ui.Color.Cyan => "text-cyan-800 bg-cyan-400/20 dark:text-cyan-200 dark:bg-cyan-400/40",
        Ui.Color.Sky => "text-sky-800 bg-sky-400/20 dark:text-sky-200 dark:bg-sky-400/40",
        Ui.Color.Blue => "text-blue-800 bg-blue-400/20 dark:text-blue-200 dark:bg-blue-400/40",
        Ui.Color.Indigo => "text-indigo-700 bg-indigo-400/20 dark:text-indigo-200 dark:bg-indigo-400/40",
        Ui.Color.Violet => "text-violet-700 bg-violet-400/20 dark:text-violet-200 dark:bg-violet-400/40",
        Ui.Color.Purple => "text-purple-700 bg-purple-400/20 dark:text-purple-200 dark:bg-purple-400/40",
        Ui.Color.Fuchsia => "text-fuchsia-700 bg-fuchsia-400/20 dark:text-fuchsia-200 dark:bg-fuchsia-400/40",
        Ui.Color.Pink => "text-pink-700 bg-pink-400/20 dark:text-pink-200 dark:bg-pink-400/40",
        Ui.Color.Rose => "text-rose-700 bg-rose-400/20 dark:text-rose-200 dark:bg-rose-400/40",
        Ui.Color.Slate => "text-slate-700 bg-slate-400/15 dark:text-slate-200 dark:bg-slate-400/40",
        Ui.Color.Gray => "text-gray-700 bg-gray-400/15 dark:text-gray-200 dark:bg-gray-400/40",
        Ui.Color.Zinc => "text-zinc-700 bg-zinc-400/15 dark:text-zinc-200 dark:bg-zinc-400/40",
        Ui.Color.Neutral => "text-neutral-700 bg-neutral-400/15 dark:text-neutral-200 dark:bg-neutral-400/40",
        Ui.Color.Stone => "text-stone-700 bg-stone-400/15 dark:text-stone-200 dark:bg-stone-400/40",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };

    /// <summary>The outlined count on a nav bar or navlist item.</summary>
    internal static string BadgeOutline(Ui.Color color) => color switch
    {
        Ui.Color.Red => "text-red-700 border border-red-400/60 dark:text-red-200",
        Ui.Color.Orange => "text-orange-700 border border-orange-400/60 dark:text-orange-200",
        Ui.Color.Amber => "text-amber-700 border border-amber-400/60 dark:text-amber-200",
        Ui.Color.Yellow => "text-yellow-800 border border-yellow-400/60 dark:text-yellow-200",
        Ui.Color.Lime => "text-lime-800 border border-lime-400/60 dark:text-lime-200",
        Ui.Color.Green => "text-green-800 border border-green-400/60 dark:text-green-200",
        Ui.Color.Emerald => "text-emerald-800 border border-emerald-400/60 dark:text-emerald-200",
        Ui.Color.Teal => "text-teal-800 border border-teal-400/60 dark:text-teal-200",
        Ui.Color.Cyan => "text-cyan-800 border border-cyan-400/60 dark:text-cyan-200",
        Ui.Color.Sky => "text-sky-800 border border-sky-400/60 dark:text-sky-200",
        Ui.Color.Blue => "text-blue-800 border border-blue-400/60 dark:text-blue-200",
        Ui.Color.Indigo => "text-indigo-700 border border-indigo-400/60 dark:text-indigo-200",
        Ui.Color.Violet => "text-violet-700 border border-violet-400/60 dark:text-violet-200",
        Ui.Color.Purple => "text-purple-700 border border-purple-400/60 dark:text-purple-200",
        Ui.Color.Fuchsia => "text-fuchsia-700 border border-fuchsia-400/60 dark:text-fuchsia-200",
        Ui.Color.Pink => "text-pink-700 border border-pink-400/60 dark:text-pink-200",
        Ui.Color.Rose => "text-rose-700 border border-rose-400/60 dark:text-rose-200",
        Ui.Color.Slate => "text-slate-700 border border-slate-400/60 dark:text-slate-200",
        Ui.Color.Gray => "text-gray-700 border border-gray-400/60 dark:text-gray-200",
        Ui.Color.Zinc => "text-zinc-700 border border-zinc-400/60 dark:text-zinc-200",
        Ui.Color.Neutral => "text-neutral-700 border border-neutral-400/60 dark:text-neutral-200",
        Ui.Color.Stone => "text-stone-700 border border-stone-400/60 dark:text-stone-200",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };

    /// <summary>An avatar's fill and ink: the hue's 200 under its 800, in both schemes.</summary>
    internal static string Avatar(Ui.Color color) => color switch
    {
        Ui.Color.Red => "bg-red-200 text-red-800",
        Ui.Color.Orange => "bg-orange-200 text-orange-800",
        Ui.Color.Amber => "bg-amber-200 text-amber-800",
        Ui.Color.Yellow => "bg-yellow-200 text-yellow-800",
        Ui.Color.Lime => "bg-lime-200 text-lime-800",
        Ui.Color.Green => "bg-green-200 text-green-800",
        Ui.Color.Emerald => "bg-emerald-200 text-emerald-800",
        Ui.Color.Teal => "bg-teal-200 text-teal-800",
        Ui.Color.Cyan => "bg-cyan-200 text-cyan-800",
        Ui.Color.Sky => "bg-sky-200 text-sky-800",
        Ui.Color.Blue => "bg-blue-200 text-blue-800",
        Ui.Color.Indigo => "bg-indigo-200 text-indigo-800",
        Ui.Color.Violet => "bg-violet-200 text-violet-800",
        Ui.Color.Purple => "bg-purple-200 text-purple-800",
        Ui.Color.Fuchsia => "bg-fuchsia-200 text-fuchsia-800",
        Ui.Color.Pink => "bg-pink-200 text-pink-800",
        Ui.Color.Rose => "bg-rose-200 text-rose-800",
        Ui.Color.Slate => "bg-slate-200 text-slate-800",
        Ui.Color.Gray => "bg-gray-200 text-gray-800",
        Ui.Color.Zinc => "bg-zinc-200 text-zinc-800",
        Ui.Color.Neutral => "bg-neutral-200 text-neutral-800",
        Ui.Color.Stone => "bg-stone-200 text-stone-800",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };

    /// <summary>The dot on an avatar: the hue's 500, a shade lighter on dark.</summary>
    internal static string AvatarBadge(Ui.Color color) => color switch
    {
        Ui.Color.Red => "bg-red-500 dark:bg-red-400",
        Ui.Color.Orange => "bg-orange-500 dark:bg-orange-400",
        Ui.Color.Amber => "bg-amber-500 dark:bg-amber-400",
        Ui.Color.Yellow => "bg-yellow-500 dark:bg-yellow-400",
        Ui.Color.Lime => "bg-lime-500 dark:bg-lime-400",
        Ui.Color.Green => "bg-green-500 dark:bg-green-400",
        Ui.Color.Emerald => "bg-emerald-500 dark:bg-emerald-400",
        Ui.Color.Teal => "bg-teal-500 dark:bg-teal-400",
        Ui.Color.Cyan => "bg-cyan-500 dark:bg-cyan-400",
        Ui.Color.Sky => "bg-sky-500 dark:bg-sky-400",
        Ui.Color.Blue => "bg-blue-500 dark:bg-blue-400",
        Ui.Color.Indigo => "bg-indigo-500 dark:bg-indigo-400",
        Ui.Color.Violet => "bg-violet-500 dark:bg-violet-400",
        Ui.Color.Purple => "bg-purple-500 dark:bg-purple-400",
        Ui.Color.Fuchsia => "bg-fuchsia-500 dark:bg-fuchsia-400",
        Ui.Color.Pink => "bg-pink-500 dark:bg-pink-400",
        Ui.Color.Rose => "bg-rose-500 dark:bg-rose-400",
        Ui.Color.Slate => "bg-slate-400 dark:bg-slate-300",
        Ui.Color.Gray => "bg-gray-400 dark:bg-gray-300",
        Ui.Color.Zinc => "bg-zinc-400 dark:bg-zinc-300",
        Ui.Color.Neutral => "bg-neutral-400 dark:bg-neutral-300",
        Ui.Color.Stone => "bg-stone-400 dark:bg-stone-300",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };
}
