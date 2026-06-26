namespace Rask.Example.Shared.Features;

// A reusable Bootstrap 5.3 toast — rendered, shown, dismissed and auto-hidden entirely by Rask state,
// with no bootstrap.bundle.js (the showcase loads Bootstrap CSS only). Bootstrap's toast normally needs
// JS for all three: `new bootstrap.Toast(el).show()` to add the `.show` class, `data-bs-dismiss="toast"`
// wired by the plugin to hide it, and `autohide`/`delay` driving a setTimeout. Here:
//   • show     — we render `class="toast show"` directly; the toast exists in the tree only while visible.
//   • dismiss  — the × is a plain Button whose OnClick fires the OnClose callback with this toast's Id.
//   • autohide — a one-shot Timer started in OnMount fires OnClose after AutoHideMs (mirrors the Disposal
//                example's UnmountTimerProbe); OnUnmount disposes it. OnClose is the single removal signal,
//                so the × and the timer share one path and there's no double-fire.
//
// Two layouts mirror the Bootstrap docs: a default toast uses the header (icon + title + timestamp + ×
// over a body), while a coloured toast (Variant set) uses the "Color schemes" headerless variant — an
// align-items-center root with a d-flex row of body + white × (Title/Timestamp are header-only).
//
// OnClose is Action<int> (the toast's Id), not Action. The host binds it as a method group
// (OnClose: RemoveToast), so the delegate's Target is the host component — which is what AutoCallback
// keys on to re-render the owner. A per-item lambda (() => Remove(t.Id)) would capture the loop local
// instead of `this`, so its target would be a closure, not the component, and no re-render would fire.
public sealed class Toast : Component
{
    // Caller-assigned identity passed back through OnClose so the host knows which toast to dismiss.
    public required int Id { get; set; }

    // Heading shown in the toast header, next to the optional icon.
    public required string Title { get; set; }

    // Body text of the toast.
    public required string Message { get; set; }

    // Optional Bootstrap colour utility applied to the toast root, e.g. "text-bg-success". When set we
    // also switch the close button to its white variant so it stays visible on the coloured background.
    public string? Variant { get; set; }

    // Optional bootstrap-icons class for the header glyph, e.g. "bi-check-circle-fill".
    public string? Icon { get; set; }

    // Optional muted timestamp shown on the right of the header, e.g. "just now".
    public string? Timestamp { get; set; }

    // Invoked with this toast's Id when it should disappear — from the × button or the auto-hide timer.
    // Bind it as a host method group so its target is the host component (see the class remark); the
    // framework then wraps it to re-render the host, which drops this toast from its list and unmounts it.
    public Action<int>? OnClose { get; set; }

    // Auto-hide delay in milliseconds. Null or <= 0 means the toast stays until dismissed by hand.
    public int? AutoHideMs { get; set; }

    private Timer? _autoHideTimer;

    protected override void OnMount()
    {
        if (AutoHideMs is > 0 && OnClose is not null)
        {
            // One-shot: dueTime = the delay, period = Infinite. Timer callbacks run on a thread-pool
            // thread; OnClose is auto-wrapped and re-renders the owner, and StateHasChanged is thread-safe,
            // so no marshalling is needed. If the toast is dismissed first, OnUnmount disposes us before we
            // fire.
            _autoHideTimer = new Timer(_ => OnClose?.Invoke(Id), null, AutoHideMs.Value, Timeout.Infinite);
        }
    }

    protected override void OnUnmount()
    {
        _autoHideTimer?.Dispose();
        _autoHideTimer = null;
    }

    protected override RenderResult Render()
    {
        var aria = new Dictionary<string, string?> { ["live"] = "assertive", ["atomic"] = "true" };

        // Coloured toast — Bootstrap's "Color schemes" headerless layout: body + white × in a flex row.
        if (Variant is not null)
        {
            return Div(Class: $"toast show align-items-center {Variant} border-0", Role: "alert", Aria: aria)[
                Div(Class: "d-flex")[
                    Div(Class: "toast-body")[
                        Icon is not null ? I(Class: $"bi {Icon} me-2") : (Child)Fragment(),
                        Message
                    ],
                    Button(
                        Type: "button",
                        Class: "btn-close btn-close-white me-2 m-auto",
                        Aria: new Dictionary<string, string?> { ["label"] = "Close" },
                        OnClick: () => OnClose?.Invoke(Id))
                ]
            ];
        }

        // Default toast — header (icon + title + timestamp + ×) over the body.
        return Div(Class: "toast show", Role: "alert", Aria: aria)[
            Div(Class: "toast-header")[
                Icon is not null ? I(Class: $"bi {Icon} me-2") : (Child)Fragment(),
                Strong(Class: "me-auto")[Title],
                Timestamp is not null ? Small(Class: "text-secondary")[Timestamp] : (Child)Fragment(),
                Button(
                    Type: "button",
                    Class: "btn-close",
                    Aria: new Dictionary<string, string?> { ["label"] = "Close" },
                    OnClick: () => OnClose?.Invoke(Id))
            ],
            Div(Class: "toast-body")[Message]
        ];
    }
}
