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
public sealed partial class BsToast : Component
{
    // Identity passed back through OnClose so the host knows which toast to remove. Also used as the
    // element id. (BsToast extends Component, not BsBlock, because its Id is an int, not BsBlock's string.)
    public required int Id { get; set; }

    public string? Class { get; set; }
    public new string? Title { get; set; }
    public required string Message { get; set; }

    // A theme color renders the headerless colored layout (text-bg-{color}); null uses the header layout.
    public BsColor? Color { get; set; }

    public BsIconName? Icon { get; set; }
    public string? Timestamp { get; set; }

    // Invoked with this toast's Id from the × or the auto-hide timer. Bind a host method group so the
    // auto-wrapped callback re-renders the host (which drops this toast and unmounts it).
    public Action<int>? OnClose { get; set; }
    public Func<int, Task>? OnCloseAsync { get; set; }

    // Auto-hide delay in ms; null/<= 0 keeps the toast until dismissed by hand.
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
