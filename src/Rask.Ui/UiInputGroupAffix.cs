namespace Rask;

/// <summary>What an input group's prefix and suffix share: the tinted, bordered box their text sits in.</summary>
internal static class UiInputGroupAffix
{
    /// <summary>zinc-800/5 on a zinc-200 border, with the input's shadow.</summary>
    internal const string Look =
        "flex items-center px-4 whitespace-nowrap shadow-xs border border-zinc-200 dark:border-white/10 "
        + "bg-zinc-800/5 dark:bg-white/20 text-zinc-800 dark:text-zinc-200";

    /// <summary>The text size of the input it sits beside.</summary>
    internal static string Size(Ui.InputSize? size) => size switch
    {
        Ui.InputSize.Sm => "text-sm",
        Ui.InputSize.Xs => "text-xs",
        _ => "text-base sm:text-sm",
    };
}
