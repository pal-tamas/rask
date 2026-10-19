namespace Rask.Dashboard.Pages;

/// <summary>Text that is code — a payload, a stack trace — kept as written, in a box that scrolls rather than stretching the page.</summary>
/// <remarks>Raw code text, which Flux has no piece for: a heading of Flux's over a plain block.</remarks>
internal sealed partial class DashboardCode : Component
{
    private const string Plain =
        "max-h-80 overflow-auto rounded-lg border border-zinc-200 bg-zinc-50 p-3 font-mono text-xs whitespace-pre-wrap wrap-anywhere "
        + "text-zinc-800 dark:border-white/10 dark:bg-white/5 dark:text-zinc-200";

    private const string Error =
        "max-h-80 overflow-auto rounded-lg border border-red-200 bg-red-50 p-3 font-mono text-xs whitespace-pre-wrap wrap-anywhere "
        + "text-red-800 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200";

    public required string Content { get; set; }

    public string? Label { get; set; }

    /// <summary>An error: the box and its text are drawn red.</summary>
    public bool? Danger { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid gap-2")[
            Label is null ? null : Ui.Heading[Label],
            Pre.Class(Danger is true ? Error : Plain)[Content]
        ];
}
