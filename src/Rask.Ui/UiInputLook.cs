namespace Rask;

/// <summary>
///     The classes Flux's input is drawn with, each written from fluxui.dev's measurements.
/// </summary>
/// <remarks>
///     Apart from <see cref="UiInput{T}" /> so the literals exist once rather than once per <c>T</c>, and so
///     <see cref="UiTextarea{T}" /> draws its box from the same border, ink and states.
/// </remarks>
internal static class UiInputLook
{
    /// <summary>The wrapper: the room the icons are placed in.</summary>
    internal const string Root = "w-full relative block";

    /// <summary>
    ///     The outlined box: white, zinc-200 with a darker bottom edge and <c>shadow-xs</c>; flat and paler
    ///     when disabled; red-500 with no shadow when invalid.
    /// </summary>
    internal const string Outline =
        "border bg-white dark:bg-white/10 dark:disabled:bg-white/[7%] "
        + "text-zinc-700 dark:text-zinc-300 disabled:text-zinc-500 dark:disabled:text-zinc-400 "
        + "placeholder:text-zinc-400 disabled:placeholder:text-zinc-400/70 dark:disabled:placeholder:text-zinc-500 "
        + "border-zinc-200 border-b-zinc-300/80 disabled:border-b-zinc-200 "
        + "dark:border-white/10 dark:border-b-white/10 dark:disabled:border-white/5 dark:disabled:border-b-white/5 "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500 disabled:data-invalid:border-red-500";

    /// <summary>The shadow under an outlined input: none in dark, when disabled or when invalid.</summary>
    internal const string InputShadow = "shadow-xs dark:shadow-none disabled:shadow-none data-invalid:shadow-none";

    /// <summary>The filled box: no border, a zinc-800/5 tint.</summary>
    internal const string Filled =
        "border-0 dark:shadow-none bg-zinc-800/5 dark:bg-white/10 text-zinc-700 dark:text-zinc-200 "
        + "placeholder:text-zinc-500 dark:placeholder:text-white/60 "
        + "data-invalid:border data-invalid:border-red-500";

    /// <summary>The <c>&lt;input&gt;</c> itself, before size and padding.</summary>
    internal const string Control = "block w-full appearance-none";

    /// <summary>A leading icon: over the box's left edge, not in the pointer's way.</summary>
    internal const string Leading =
        "pointer-events-none absolute top-0 bottom-0 start-0 flex items-center justify-center ps-3 text-xs "
        + "border-s border-transparent text-zinc-400/75 dark:text-white/60";

    /// <summary>What sits at the end: an icon, a shortcut, the clear and reveal buttons.</summary>
    internal const string Trailing =
        "absolute top-0 bottom-0 end-0 flex items-center gap-x-1.5 pe-2 text-xs border-e border-transparent text-zinc-400";

    /// <summary>The 32px borderless button inside the box (Flux's small subtle button).</summary>
    internal const string Action =
        "relative flex items-center justify-center gap-2 size-8 -mx-1.5 rounded-md text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 "
        + "dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The clear button: gone while the input shows its placeholder, which is while it is empty.</summary>
    internal const string Clear = "[[data-ui-input]:has(input:placeholder-shown)_&]:hidden";

    /// <summary>The tick of the copy button: there for the two seconds its button says it copied.</summary>
    internal const string Copied = "hidden in-data-copied:block";

    /// <summary>The copy button's own icon, which the tick stands in for meanwhile.</summary>
    internal const string NotCopied = "in-data-copied:hidden";

    /// <summary>The input drawn as a button: the same box, laid out as a row.</summary>
    internal const string AsButton =
        "relative flex w-full h-10 py-2 text-base sm:text-sm leading-[1.375rem] rounded-lg " + Outline + " " + InputShadow;

    /// <summary>The placeholder of the input drawn as a button.</summary>
    internal const string AsButtonText = "block flex-1 text-start font-medium text-zinc-400 dark:text-white/40";

    /// <summary>The leading icon of the input drawn as a button, inside its border.</summary>
    internal const string AsButtonLeading =
        "absolute top-0 bottom-0 start-0 flex items-center justify-center ps-3 text-xs text-zinc-400/75";

    /// <summary>The shortcut of the input drawn as a button.</summary>
    internal const string AsButtonKbd =
        "absolute top-0 bottom-0 end-0 flex items-center justify-center pe-4 text-xs text-zinc-400/75";

    /// <summary>A file input's shell: the real input out of sight, a button and the chosen name in a row.</summary>
    internal const string File = "relative flex items-center gap-4 cursor-auto";

    /// <summary>"Choose file", drawn as Flux's outline button.</summary>
    internal const string FileButton =
        "relative flex items-center justify-center gap-2 h-10 px-4 rounded-lg text-sm font-medium whitespace-nowrap cursor-pointer "
        + "border border-zinc-200 border-b-zinc-300/80 hover:border-b-zinc-200 dark:border-zinc-600 dark:border-b-zinc-600 dark:hover:border-b-zinc-600 "
        + "bg-white hover:bg-zinc-50 dark:bg-zinc-700 dark:hover:bg-zinc-600/75 text-zinc-800 dark:text-white shadow-xs";

    /// <summary>The chosen file's name, cut short with an ellipsis.</summary>
    internal const string FileName =
        "cursor-default select-none overflow-hidden text-ellipsis whitespace-nowrap text-sm font-medium "
        + "text-zinc-500 dark:text-zinc-400";

    /// <summary>What marks a file input's real <c>&lt;input&gt;</c>.</summary>
    internal static readonly Dictionary<string, string?> FileMarks = new(StringComparer.Ordinal)
    {
        ["data-ui-control"] = null,
    };

    private static readonly Dictionary<string, string?> Input = new(StringComparer.Ordinal)
    {
        ["data-ui-control"] = null,
        ["data-ui-group-target"] = null,
    };

    private static readonly Dictionary<string, string?> InvalidInput = new(Input, StringComparer.Ordinal)
    {
        ["data-invalid"] = null,
    };

    private static readonly Dictionary<string, string?> Textarea = new(StringComparer.Ordinal)
    {
        ["data-ui-control"] = null,
        ["data-ui-textarea"] = null,
    };

    private static readonly Dictionary<string, string?> InvalidTextarea = new(Textarea, StringComparer.Ordinal)
    {
        ["data-invalid"] = null,
    };

    /// <summary>What marks the <c>&lt;input&gt;</c>: Flux's <c>data-flux-control</c> and, when invalid, <c>data-invalid</c>.</summary>
    internal static Dictionary<string, string?> Marks(bool invalid) => invalid ? InvalidInput : Input;

    /// <summary>What marks the <c>&lt;input&gt;</c>, and after it the attributes the call site forwards to it.</summary>
    internal static IReadOnlyDictionary<string, string?> Marks(bool invalid, IReadOnlyDictionary<string, string?>? forwarded)
    {
        var marks = Marks(invalid);
        if (forwarded is null || forwarded.Count == 0)
        {
            return marks;
        }

        var all = new Dictionary<string, string?>(marks, StringComparer.Ordinal);
        foreach (var (name, value) in forwarded)
        {
            all[name] = value;
        }

        return all;
    }

    /// <summary>What marks the <c>&lt;textarea&gt;</c>.</summary>
    internal static Dictionary<string, string?> TextareaMarks(bool invalid) => invalid ? InvalidTextarea : Textarea;

    /// <summary>Height, text and corners.</summary>
    internal static string Size(Ui.InputSize size) => size switch
    {
        Ui.InputSize.Sm => "h-8 py-1.5 text-sm leading-[1.125rem] rounded-md",
        Ui.InputSize.Xs => "h-6 py-1.5 text-xs leading-[1.125rem] rounded-md",
        _ => "h-10 py-2 text-base sm:text-sm leading-[1.375rem] rounded-lg",
    };

    /// <summary>12px at a bare end, 40px at one that holds an icon or a button.</summary>
    internal static string Padding(bool leading, bool trailing) => (leading, trailing) switch
    {
        (true, true) => "ps-10 pe-10",
        (true, false) => "ps-10 pe-3",
        (false, true) => "ps-3 pe-10",
        _ => "ps-3 pe-3",
    };

    /// <summary>The box by variant.</summary>
    internal static string Variant(Ui.InputVariant variant) =>
        variant == Ui.InputVariant.Filled ? Filled : Outline + " " + InputShadow;
}
