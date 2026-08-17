using System.Threading;

namespace Rask.Bootstrap;

// A Bootstrap toast — shown, dismissed and auto-hidden entirely by Rask state, with no
// bootstrap.bundle.js. Designed for a host-managed stack: each toast carries an Id passed back through
// OnClose (bind a host method group, e.g. OnClose: RemoveToast, so the auto-wrapped callback re-renders
// the host that owns the list). Position a .toast-container yourself (Bootstrap's placement utilities).
//
//   • show     — renders class="toast show"; the toast lives in the tree only while visible.
//   • dismiss  — the × (BsCloseButton) invokes OnClose with this toast's Id.
//   • autohide — a one-shot timer started in OnMount fires OnClose after AutoHideMs; OnUnmount disposes it.
//
// Two layouts mirror the Bootstrap docs: Color set → the "Color schemes" headerless variant (body + white
// ×); otherwise a header (icon + title + timestamp + ×) over the body.

/// <summary>
///     A transient notification. Never put an action here that exists nowhere else — a toast disappears,
///     and anyone who needs longer to read it loses it.
/// </summary>
public sealed partial class BsToast : Component
{
    // Identity passed back through OnClose so the host knows which toast to remove. Also used as the
    // element id. (BsToast extends Component, not BsBlock, because its Id is an int, not BsBlock's string.)
    /// <summary>
    ///     This toast's identity, handed back to <c>OnClose</c> so the host knows which one to remove from
    ///     its stack — and used as the rendered element's <c>id</c>. Required: a stack of toasts that
    ///     cannot tell its members apart cannot dismiss one of them.
    /// </summary>
    public required int Id { get; set; }

    /// <summary>
    ///     Extra classes, added alongside the toast's own Bootstrap classes rather than replacing them.
    /// </summary>
    public string? Class { get; set; }

    /// <summary>The toast's heading.</summary>
    public new string? Title { get; set; }

    /// <summary>The toast's body text.</summary>
    public required string Message { get; set; }

    // A theme color renders the headerless colored layout (text-bg-{color}); null uses the header layout.

    /// <summary>The semantic colour.</summary>
    public BsColor? Color { get; set; }

    /// <summary>An icon shown beside the title.</summary>
    public BsIconName? Icon { get; set; }

    /// <summary>A relative time shown in the header.</summary>
    public string? Timestamp { get; set; }

    // Invoked with this toast's Id from the × or the auto-hide timer. Bind a host method group so the
    // auto-wrapped callback re-renders the host (which drops this toast and unmounts it).

    /// <summary>Runs when the toast is dismissed.</summary>
    public Action<int>? OnClose { get; set; }

    /// <summary>Runs when the toast is dismissed, asynchronously.</summary>
    public Func<int, Task>? OnCloseAsync { get; set; }

    // Auto-hide delay in ms; null/<= 0 keeps the toast until dismissed by hand.

    /// <summary>
    ///     How long before it dismisses itself, in milliseconds. Give the reader time — and never auto-hide
    ///     an error.
    /// </summary>
    public int? AutoHideMs { get; set; }

    private Timer? _autoHideTimer;

    private static readonly IReadOnlyDictionary<string, string?> ToastAria =
        new Dictionary<string, string?> { ["live"] = "assertive", ["atomic"] = "true" };

    protected override void OnMount()
    {
        if (AutoHideMs is > 0 && (OnClose is not null || OnCloseAsync is not null))
        {
            _autoHideTimer = new Timer(_ => Dismiss(), null, AutoHideMs.Value, Timeout.Infinite);
        }
    }

    protected override void OnUnmount()
    {
        _autoHideTimer?.Dispose();
        _autoHideTimer = null;
    }

    private void Dismiss()
    {
        OnClose?.Invoke(Id);
        _ = OnCloseAsync?.Invoke(Id);
    }

    protected override Component? Render()
    {
        var close = BsCloseButton
            .White(Color is not null)
            .Class(Color is not null ? "me-2 m-auto" : null)
            .OnClick(OnClose is null ? null : () => OnClose?.Invoke(Id))
            .OnClickAsync(OnCloseAsync is null
                ? null
                : () => OnCloseAsync?.Invoke(Id) ?? Task.CompletedTask);

        // Colored "Color schemes" layout — headerless, body + white × in a flex row.
        if (Color is { } color)
        {
            return Div
                .Id(Id.ToString())
                .Class(BsClass.Join("toast show align-items-center", color.TextBg(), "border-0", Class))
                .Role("alert")
                .Aria(ToastAria)[
                Div.Class("d-flex")[
                    Div.Class("toast-body")[
                        Icon is { } glyph ? BsIcon.Name(glyph).Class("me-2") : null,
                        Message
                    ],
                    close
                ]
            ];
        }

        // Default layout — header (icon + title + timestamp + ×) over the body.
        return Div.Id(Id.ToString()).Class(BsClass.Join("toast show", Class)).Role("alert").Aria(ToastAria)[
            Div.Class("toast-header")[
                Icon is { } headerGlyph ? BsIcon.Name(headerGlyph).Class("me-2") : null,
                Strong.Class("me-auto")[Title ?? ""],
                Timestamp is { } ts ? Small.Class("text-secondary")[ts] : null,
                close
            ],
            Div.Class("toast-body")[Message]
        ];
    }
}
