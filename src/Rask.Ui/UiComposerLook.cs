namespace Rask;

/// <summary>
///     The classes Flux's composer is drawn with, each written from fluxui.dev's measurements, and the marks its
///     root carries. Apart from <see cref="UiComposer{T}" /> so the literals exist once rather than once per <c>T</c>.
/// </summary>
internal static class UiComposerLook
{
    /// <summary>
    ///     A grid of four columns — the outer two as wide as the inline buttons, so they are nothing otherwise — in
    ///     the input's box: white, zinc-200 with a darker bottom edge, <c>shadow-xs</c>; flat and paler disabled;
    ///     red-500 invalid, the shadow kept.
    /// </summary>
    internal const string Root =
        "grid grid-cols-[auto_1fr_1fr_auto] p-2 border border-zinc-200 border-b-zinc-300/80 bg-white shadow-xs "
        + "dark:border-white/10 dark:border-b-white/10 dark:bg-white/10 "
        + "[&[disabled]]:shadow-none dark:[&[disabled]]:bg-white/[7%] "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500";

    /// <summary>The message box's corners.</summary>
    internal const string Corners = "rounded-2xl";

    /// <summary>The corners of the other inputs (<c>variant="input"</c>).</summary>
    internal const string InputCorners = "rounded-lg";

    /// <summary>The message box rounds its buttons a step more than a small button's own corners.</summary>
    internal const string RoundButtons = "**:data-ui-button:rounded-lg";

    /// <summary>A disabled composer's buttons: fainter than a disabled button alone, and flat.</summary>
    internal const string LockedButtons = "**:data-ui-button:opacity-50 **:data-ui-button:shadow-none";

    /// <summary>Above the box, three columns wide as Flux has it (the fourth is empty unless inline).</summary>
    internal const string Header = "col-span-3 flex items-center gap-1 mb-2";

    /// <summary>The header's place when there is nothing to put in it.</summary>
    internal const string NoHeader = "col-span-3 hidden";

    /// <summary>The box, the whole width.</summary>
    internal const string Box = "col-span-4";

    /// <summary>The box between the buttons, inline.</summary>
    internal const string InlineBox = "col-start-2 col-span-2";

    /// <summary>The leading buttons, under the box at its start.</summary>
    internal const string Leading = "col-span-2 flex items-start gap-1";

    /// <summary>The leading buttons, inline: before the box.</summary>
    internal const string InlineLeading = "col-start-1 row-start-1 flex items-start gap-1";

    /// <summary>The trailing buttons, under the box at its end.</summary>
    internal const string Trailing = "col-span-2 flex items-start justify-end gap-1";

    /// <summary>The trailing buttons, inline: after the box.</summary>
    internal const string InlineTrailing = "flex items-start justify-end gap-1";

    /// <summary>Under the buttons, the whole width. No example of Flux's shows one, so it has no spacing of its own.</summary>
    internal const string Footer = "col-span-4";

    /// <summary>The footer's place when there is nothing to put in it.</summary>
    internal const string NoFooter = "col-span-4 hidden";

    /// <summary>
    ///     The <c>&lt;textarea&gt;</c>: no box of its own, the input's ink, no scrollbar until the runtime has grown
    ///     it to its last line (<c>data-rask-grow</c>).
    /// </summary>
    internal const string Text =
        "block w-full resize-none overflow-y-hidden px-2 py-1.5 outline-none text-base sm:text-sm "
        + "text-zinc-700 dark:text-zinc-300 disabled:text-zinc-500 dark:disabled:text-zinc-400 "
        + "placeholder:text-zinc-400 disabled:placeholder:text-zinc-400/70 dark:disabled:placeholder:text-zinc-500";

    /// <summary>Flux's <c>disabled</c> on the composer itself, which its look keys on.</summary>
    internal static readonly Dictionary<string, string?> Locked = new(StringComparer.Ordinal) { ["disabled"] = null };

    private static readonly Dictionary<string, string?> ModEnter = Marked("Mod+Enter", invalid: false);
    private static readonly Dictionary<string, string?> ModEnterInvalid = Marked("Mod+Enter", invalid: true);
    private static readonly Dictionary<string, string?> Enter = Marked("Enter", invalid: false);
    private static readonly Dictionary<string, string?> EnterInvalid = Marked("Enter", invalid: true);

    /// <summary>The root's <c>data-*</c>: the marker, the key that submits (the runtime's), and <c>data-invalid</c>.</summary>
    internal static Dictionary<string, string?> Marks(Ui.ComposerSubmit submit, bool invalid) => (submit, invalid) switch
    {
        (Ui.ComposerSubmit.Enter, true) => EnterInvalid,
        (Ui.ComposerSubmit.Enter, false) => Enter,
        (_, true) => ModEnterInvalid,
        _ => ModEnter,
    };

    /// <summary>Locks every <c>Ui.Button</c> in a slot, as Flux's disabled composer does.</summary>
    internal static void Disable(Component? slot)
    {
        if (slot is UiButton button)
        {
            button.Disabled = true;
            button.LockedByComposer = true;
            BuilderRuntime.MarkChanged(button);
            return;
        }

        foreach (var child in slot?.Children ?? [])
        {
            Disable(child);
        }
    }

    private static Dictionary<string, string?> Marked(string keys, bool invalid)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-composer"] = null, ["rask-submit-keys"] = keys };
        if (invalid)
        {
            marks["invalid"] = null;
        }

        return marks;
    }
}
