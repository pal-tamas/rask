namespace Rask.Example.Shared.Features;

// The live host: owns the stack of visible toasts and the demo controls (variant, placement, auto-hide).
// Every interaction is a plain field mutation behind a Button OnClick — the live diff re-renders the
// toast-container. Each Toast carries Key so the keyed-list diff tracks identity as toasts come and go.
public sealed class ToastDemo : Component
{
    private readonly record struct ToastModel(int Id, string Title, string Message, BsColor? Color, BsIconName? Icon);

    // Available corner/edge placements, mapped to the Bootstrap utility classes that position the
    // toast-container. In a real app the container is position-fixed over the viewport; here it's
    // position-absolute inside a relative stage so placement is shown within the demo, not over the page.
    private static readonly (string Label, string Class)[] Placements =
    [
        ("Top start", "top-0 start-0"),
        ("Top center", "top-0 start-50 translate-middle-x"),
        ("Top end", "top-0 end-0"),
        ("Bottom start", "bottom-0 start-0"),
        ("Bottom center", "bottom-0 start-50 translate-middle-x"),
        ("Bottom end", "bottom-0 end-0")
    ];

    // Auto-hide timers fire on thread-pool threads and mutate _toasts via OnClose, while a render may be
    // enumerating it — so every read/write is guarded (same discipline as the framework's timer demos).
    private readonly object _gate = new();
    private readonly List<ToastModel> _toasts = [];
    private int _nextId;
    private bool _autoHide;
    private string _placement = "top-0 end-0";

    private void Add(string title, string message, BsColor? color, BsIconName? icon)
    {
        lock (_gate)
        {
            _toasts.Add(new ToastModel(_nextId++, title, message, color, icon));
        }
    }

    // A method group (OnClose: RemoveToast) so the delegate's target is this host component — which is what
    // AutoCallback keys on to re-render the owner. A per-toast lambda would capture the loop local instead.
    private void RemoveToast(int id)
    {
        lock (_gate)
        {
            _toasts.RemoveAll(x => x.Id == id);
        }
    }

    private void Clear()
    {
        lock (_gate)
        {
            _toasts.Clear();
        }
    }

    private ToastModel[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _toasts];
        }
    }

    protected override RenderResult Render()
    {
        var toasts = Snapshot();
        return Div()[
            // Trigger buttons — each pushes a toast onto the stack.
            Div(Class: "d-flex flex-wrap gap-2")[
                BsButton(Color: BsColor.Primary, OnClick: () =>
                    Add("Rask", "Hello, world! This is a toast message.", null, null))[
                    BsIcon(Name: BsIconName.Bell, Class: "me-1"), "Show toast"],
                BsButton(Color: BsColor.Success, OnClick: () =>
                    Add("Saved", "Your changes were saved.", BsColor.Success, BsIconName.CheckCircleFill))["Success"],
                BsButton(Color: BsColor.Danger, OnClick: () =>
                    Add("Error", "Something went wrong.", BsColor.Danger, BsIconName.ExclamationTriangleFill))["Danger"],
                BsButton(Color: BsColor.Warning, OnClick: () =>
                    Add("Heads up", "Double-check your input.", BsColor.Warning, BsIconName.ExclamationCircleFill))[
                    "Warning"],
                BsButton(Color: BsColor.Secondary, Outline: true, Class: "ms-auto", OnClick: () => Clear())[
                    BsIcon(Name: BsIconName.Trash, Class: "me-1"), "Clear all"]
            ],

            // Options — auto-hide toggle + placement picker.
            Div(Class: "d-flex flex-wrap align-items-center gap-3 mt-3")[
                Button(
                    Class: _autoHide ? "btn btn-sm btn-success" : "btn btn-sm btn-outline-secondary",
                    OnClick: () => _autoHide = !_autoHide)[
                    I(Class: $"bi {(_autoHide ? "bi-check-square" : "bi-square")} me-1"), "Auto-hide (5s)"],
                Div(Class: "btn-group btn-group-sm", Role: "group")[
                    Placements.Select(p => (Child)Button(
                        Class: _placement == p.Class ? "btn btn-secondary" : "btn btn-outline-secondary",
                        OnClick: () => _placement = p.Class,
                        Key: p.Class)[p.Label])
                ]
            ],

            // The stage. position-relative so the position-absolute toast-container anchors to it.
            Div(Class: "position-relative border rounded bg-body-tertiary mt-3 overflow-hidden",
                Style: "min-height: 360px")[
                toasts.Length == 0
                    ? Div(Class: "position-absolute top-50 start-50 translate-middle text-secondary small")[
                        "No toasts — click a button above."]
                    : (Child)Fragment(),
                Div(Class: $"toast-container position-absolute {_placement} p-3")[
                    toasts.Select(t => (Child)BsToast(
                        Id: t.Id,
                        Title: t.Title,
                        Message: t.Message,
                        Color: t.Color,
                        Icon: t.Icon,
                        Timestamp: "just now",
                        AutoHideMs: _autoHide ? 5000 : null,
                        OnClose: RemoveToast,
                        Key: t.Id.ToString()))
                ]
            ]
        ];
    }
}
