using System.Globalization;
using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

// Live demo for the full GlobalEventHandlers surface. Every handler below mutates a field and the
// framework re-renders THIS component automatically (the handler's closure captures `this`), so there
// is not a single StateHasChanged in here — derived readouts just update. One element, many events.
public sealed partial class EventsDemo : Component
{
    private double _x, _y;
    private bool _hovering;
    private int _wheel;
    private int _doubleClicks;
    private bool _contextMenu;
    private bool _focused;
    private string _lastKey = "—";
    private string _pasted = "—";

    private static string Fmt(double d) => d.ToString("0", CultureInfo.InvariantCulture);

    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            // Pointer tracking pad: mousemove + enter/leave + wheel, all typed.
            Div.Class("md:col-span-6")[
                Div
                    .Class("border rounded p-4 text-center user-select-none")
                    .Style(_hovering ? "background:#eef6ff" : null)
                    .OnMouseMove(e => { _x = e.OffsetX; _y = e.OffsetY; })
                    .OnMouseEnter(_ => _hovering = true)
                    .OnMouseLeave(_ => _hovering = false)
                    .OnWheel(e => _wheel += (int)e.DeltaY)[
                    Strong["Move / scroll here"],
                    Div.Class("text-ui-muted mt-2")[
                        $"x: {Fmt(_x)}, y: {Fmt(_y)} · {(_hovering ? "inside" : "outside")} · wheel Σ {_wheel}"]
                ]
            ],
            // Double-click + context menu (preventDefault'd client-side so the native menu is suppressed).
            Div.Class("md:col-span-6")[
                Button
                    .Class($"{Tw.BtnOutlinePrimary} w-full py-4")
                    .OnDoubleClick(_ => _doubleClicks++)
                    .OnContextMenu(_ => _contextMenu = !_contextMenu)[
                    "Double-click or right-click me"],
                Div.Class("text-ui-muted mt-2")[
                    $"double-clicks: {_doubleClicks} · context-menu toggled: {_contextMenu}"]
            ],
            // Focus / blur + keyboard on a focusable div.
            Div.Class("md:col-span-6")[
                Div
                    .Class("border rounded p-4")
                    .TabIndex(0)
                    .Style(_focused ? "outline:2px solid #0d6efd" : null)
                    .OnFocus(() => _focused = true)
                    .OnBlur(() => _focused = false)
                    .OnKeyDown(e => _lastKey = e.Key)[
                    Strong["Click to focus, then type"],
                    Div.Class("text-ui-muted mt-2")[
                        $"{(_focused ? "focused" : "blurred")} · last key: {_lastKey}"]
                ]
            ],
            // Clipboard: paste into the box and read the text server-side.
            Div.Class("md:col-span-6")[
                Div
                    .Class("border rounded p-4")
                    .OnPaste(e => _pasted = e.Text)[
                    Strong["Paste text here"],
                    Div.Class("text-ui-muted mt-2")[$"pasted: {_pasted}"]
                ]
            ]
        ];
}
