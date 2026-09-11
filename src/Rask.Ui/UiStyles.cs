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

    /// <summary>Monospace, for ids and payloads.</summary>
    public const string Mono = "font-mono text-xs";
}
