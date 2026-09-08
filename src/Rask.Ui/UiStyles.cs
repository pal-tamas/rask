namespace Rask.Ui;

/// <summary>
/// The kit's class-name vocabulary, for markup a component does not cover.
/// </summary>
/// <remarks>
/// Constants rather than <c>@apply</c>: <c>@apply</c> moves the decision into a stylesheet Tailwind
/// then has to be told about, while a constant is read by the compiler, renamed by the IDE, and found
/// by Tailwind's scanner like any other literal. Use a component where one exists — these are for the
/// gaps between them.
/// </remarks>
public static class UiStyles
{
    /// <summary>A panel: hairline border, no shadow, and padding that tightens on a phone.</summary>
    public const string Card = "rounded-xl border border-base-300 bg-base-100 p-4 sm:p-5";

    /// <summary>The small muted label above a value.</summary>
    public const string Label = "text-xs font-medium tracking-wide opacity-60";

    /// <summary>A headline number. Tabular figures so a polling value does not jitter as digits change.</summary>
    public const string Value = "mt-2 text-2xl font-semibold tabular-nums tracking-tight text-base-content sm:text-3xl";

    /// <summary>The quiet line under a value.</summary>
    public const string Caption = "mt-1 text-xs opacity-60";

    /// <summary>A page's own heading.</summary>
    public const string Heading = "text-base font-semibold tracking-tight text-base-content sm:text-lg";

    // min-h-11 below sm on every control: 44px is the smallest reliable touch target, and these are all
    // text-xs. The height relaxes from sm up, where there is a pointer.

    /// <summary>A secondary action.</summary>
    public const string Button =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-base-300 "
        + "bg-base-100 px-3 text-xs font-medium text-base-content transition-colors hover:bg-base-200 "
        + "disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-primary sm:min-h-0 sm:py-1.5";

    /// <summary>An action that destroys or re-runs work — the only colour on the console.</summary>
    public const string Danger =
        "inline-flex min-h-11 shrink-0 items-center justify-center gap-1.5 rounded-lg border border-base-300 "
        + "bg-base-100 px-3 text-xs font-medium text-error transition-colors hover:border-ui-danger/40 "
        + "hover:bg-error/5 disabled:pointer-events-none disabled:opacity-40 focus-visible:outline-2 "
        + "focus-visible:outline-offset-2 focus-visible:outline-primary sm:min-h-0 sm:py-1.5";

    /// <summary>A borderless control: a dismiss, a close.</summary>
    public const string Quiet =
        "inline-flex min-h-11 items-center rounded-lg px-2 text-xs opacity-60 hover:bg-base-200 "
        + "hover:text-base-content sm:min-h-0 sm:py-1";

    /// <summary>Monospace, for ids and payloads.</summary>
    public const string Mono = "font-mono text-xs";
}
