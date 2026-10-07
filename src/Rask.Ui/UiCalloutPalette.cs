namespace Rask;

/// <summary>
///     What a <see cref="UiCallout" /> of each colour is painted with: surface, border, and the ink of its
///     heading, text and icon — in light and in dark.
/// </summary>
/// <remarks>
///     <para>
///     Measured from Flux's page hue by hue, because there is no one formula: the border is the hue's 200
///     except where that would vanish on the 50 surface (amber, yellow and lime take 400, green and purple
///     300), red's icon is a 400 where the others are 500, and the heading and text shades move with how
///     dark the hue reads (purple 800/700, lime 700/600, yellow 600/700, red 700/700).
///     </para>
///     <para>
///     The ink is written on the callout and reaches its parts by their <c>data-slot</c>, so a
///     <see cref="UiCalloutHeading" /> or <see cref="UiCalloutText" /> needs no colour of its own and
///     takes its callout's wherever it sits inside it.
///     </para>
/// </remarks>
internal static class UiCalloutPalette
{
    // Flux's callout with no variant and no colour: the zinc one, on white rather than on zinc-50.
    internal const string Plain = "bg-white border-zinc-200 dark:bg-zinc-400/10 dark:border-white/5 " + ZincInk;

    private const string Zinc = "bg-zinc-50 border-zinc-200 dark:bg-zinc-400/10 dark:border-white/5 " + ZincInk;

    private const string ZincInk =
        "[&_[data-slot=heading]]:text-zinc-800 dark:[&_[data-slot=heading]]:text-zinc-200 "
        + "[&_[data-slot=text]]:text-zinc-500 dark:[&_[data-slot=text]]:text-zinc-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-zinc-400";

    // Tailwind's seventeen hues, in the order Ui.Color lists them.
    private static readonly string[] Hues =
    [
        // Red
        "bg-red-50 border-red-200 dark:bg-red-400/10 dark:border-red-400/50 "
        + "[&_[data-slot=heading]]:text-red-700 dark:[&_[data-slot=heading]]:text-red-200 "
        + "[&_[data-slot=text]]:text-red-700 dark:[&_[data-slot=text]]:text-red-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-red-400",
        // Orange
        "bg-orange-50 border-orange-200 dark:bg-orange-400/10 dark:border-orange-400/50 "
        + "[&_[data-slot=heading]]:text-orange-600 dark:[&_[data-slot=heading]]:text-orange-200 "
        + "[&_[data-slot=text]]:text-orange-600 dark:[&_[data-slot=text]]:text-orange-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-orange-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-orange-400",
        // Amber
        "bg-amber-50 border-amber-400 dark:bg-amber-400/10 dark:border-amber-400/50 "
        + "[&_[data-slot=heading]]:text-amber-600 dark:[&_[data-slot=heading]]:text-amber-200 "
        + "[&_[data-slot=text]]:text-amber-600 dark:[&_[data-slot=text]]:text-amber-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-amber-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-amber-400",
        // Yellow
        "bg-yellow-50 border-yellow-400 dark:bg-yellow-400/10 dark:border-yellow-400/50 "
        + "[&_[data-slot=heading]]:text-yellow-600 dark:[&_[data-slot=heading]]:text-yellow-200 "
        + "[&_[data-slot=text]]:text-yellow-700 dark:[&_[data-slot=text]]:text-yellow-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-yellow-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-yellow-400",
        // Lime
        "bg-lime-50 border-lime-400 dark:bg-lime-400/10 dark:border-lime-400/50 "
        + "[&_[data-slot=heading]]:text-lime-700 dark:[&_[data-slot=heading]]:text-lime-200 "
        + "[&_[data-slot=text]]:text-lime-600 dark:[&_[data-slot=text]]:text-lime-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-lime-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-lime-400",
        // Green
        "bg-green-50 border-green-300 dark:bg-green-400/10 dark:border-green-400/50 "
        + "[&_[data-slot=heading]]:text-green-600 dark:[&_[data-slot=heading]]:text-green-200 "
        + "[&_[data-slot=text]]:text-green-600 dark:[&_[data-slot=text]]:text-green-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-green-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-green-400",
        // Emerald
        "bg-emerald-50 border-emerald-200 dark:bg-emerald-400/10 dark:border-emerald-400/50 "
        + "[&_[data-slot=heading]]:text-emerald-600 dark:[&_[data-slot=heading]]:text-emerald-200 "
        + "[&_[data-slot=text]]:text-emerald-600 dark:[&_[data-slot=text]]:text-emerald-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-emerald-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-emerald-400",
        // Teal
        "bg-teal-50 border-teal-200 dark:bg-teal-400/10 dark:border-teal-400/50 "
        + "[&_[data-slot=heading]]:text-teal-600 dark:[&_[data-slot=heading]]:text-teal-200 "
        + "[&_[data-slot=text]]:text-teal-600 dark:[&_[data-slot=text]]:text-teal-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-teal-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-teal-400",
        // Cyan
        "bg-cyan-50 border-cyan-200 dark:bg-cyan-400/10 dark:border-cyan-400/50 "
        + "[&_[data-slot=heading]]:text-cyan-600 dark:[&_[data-slot=heading]]:text-cyan-200 "
        + "[&_[data-slot=text]]:text-cyan-600 dark:[&_[data-slot=text]]:text-cyan-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-cyan-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-cyan-400",
        // Sky
        "bg-sky-50 border-sky-200 dark:bg-sky-400/10 dark:border-sky-400/50 "
        + "[&_[data-slot=heading]]:text-sky-600 dark:[&_[data-slot=heading]]:text-sky-200 "
        + "[&_[data-slot=text]]:text-sky-600 dark:[&_[data-slot=text]]:text-sky-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-sky-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-sky-400",
        // Blue
        "bg-blue-50 border-blue-200 dark:bg-blue-400/10 dark:border-blue-400/50 "
        + "[&_[data-slot=heading]]:text-blue-600 dark:[&_[data-slot=heading]]:text-blue-200 "
        + "[&_[data-slot=text]]:text-blue-600 dark:[&_[data-slot=text]]:text-blue-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-blue-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-blue-400",
        // Indigo
        "bg-indigo-50 border-indigo-200 dark:bg-indigo-400/10 dark:border-indigo-400/50 "
        + "[&_[data-slot=heading]]:text-indigo-600 dark:[&_[data-slot=heading]]:text-indigo-200 "
        + "[&_[data-slot=text]]:text-indigo-600 dark:[&_[data-slot=text]]:text-indigo-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-indigo-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-indigo-400",
        // Violet
        "bg-violet-50 border-violet-200 dark:bg-violet-400/10 dark:border-violet-400/50 "
        + "[&_[data-slot=heading]]:text-violet-600 dark:[&_[data-slot=heading]]:text-violet-200 "
        + "[&_[data-slot=text]]:text-violet-600 dark:[&_[data-slot=text]]:text-violet-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-violet-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-violet-400",
        // Purple
        "bg-purple-50 border-purple-300 dark:bg-purple-400/10 dark:border-purple-400/50 "
        + "[&_[data-slot=heading]]:text-purple-800 dark:[&_[data-slot=heading]]:text-purple-200 "
        + "[&_[data-slot=text]]:text-purple-700 dark:[&_[data-slot=text]]:text-purple-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-purple-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-purple-400",
        // Fuchsia
        "bg-fuchsia-50 border-fuchsia-200 dark:bg-fuchsia-400/10 dark:border-fuchsia-400/50 "
        + "[&_[data-slot=heading]]:text-fuchsia-600 dark:[&_[data-slot=heading]]:text-fuchsia-200 "
        + "[&_[data-slot=text]]:text-fuchsia-600 dark:[&_[data-slot=text]]:text-fuchsia-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-fuchsia-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-fuchsia-400",
        // Pink
        "bg-pink-50 border-pink-200 dark:bg-pink-400/10 dark:border-pink-400/50 "
        + "[&_[data-slot=heading]]:text-pink-600 dark:[&_[data-slot=heading]]:text-pink-200 "
        + "[&_[data-slot=text]]:text-pink-600 dark:[&_[data-slot=text]]:text-pink-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-pink-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-pink-400",
        // Rose
        "bg-rose-50 border-rose-200 dark:bg-rose-400/10 dark:border-rose-400/50 "
        + "[&_[data-slot=heading]]:text-rose-600 dark:[&_[data-slot=heading]]:text-rose-200 "
        + "[&_[data-slot=text]]:text-rose-600 dark:[&_[data-slot=text]]:text-rose-300 "
        + "[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-rose-500 dark:[:is(&>div,&_[data-slot=heading])>[data-slot=icon]]:text-rose-400",
    ];

    internal static string For(Ui.CalloutVariant variant) => variant switch
    {
        Ui.CalloutVariant.Success => For(Ui.Color.Green),
        Ui.CalloutVariant.Warning => For(Ui.Color.Yellow),
        Ui.CalloutVariant.Danger => For(Ui.Color.Red),
        _ => Zinc,
    };

    // Slate, gray, zinc, neutral and stone follow the hues: zinc is the one grey Flux's callout is drawn in.
    internal static string For(Ui.Color color) => color <= Ui.Color.Rose ? Hues[(int)color] : Zinc;
}
