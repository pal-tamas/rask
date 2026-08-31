using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IScreenOrientation" /> — read the current screen orientation and, for an installed or
///     fullscreen app, lock/unlock it. Locking is usually rejected outside fullscreen and is often
///     unsupported on desktop, so each call is wrapped in try/catch.
/// </summary>
public sealed partial class OrientationDemo(IScreenOrientation orientation) : Component
{
    private string? _current;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button.Class(Ui.BtnPrimary).Id("orientation-read").OnClickAsync(Read)["Read current"],
                    Button
                        .Class(Ui.BtnOutlinePrimary)
                        .Id("orientation-portrait")
                        .OnClickAsync(() => Lock(OrientationLock.Portrait))["Lock portrait"],
                    Button
                        .Class(Ui.BtnOutlinePrimary)
                        .Id("orientation-landscape")
                        .OnClickAsync(() => Lock(OrientationLock.Landscape))["Lock landscape"],
                    Button.Class(Ui.BtnOutlineDanger).Id("orientation-unlock").OnClickAsync(Unlock)[
                        "Unlock"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-1")[
                    "Current: ", Code.Id("orientation-current")[_current ?? "(read to see)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("orientation-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            if (!await orientation.IsSupportedAsync())
            {
                _current = "not supported";
                return;
            }

            var info = await orientation.GetAsync();
            _current = $"{info.Type} ({info.Angle}°)";
        }
        catch (Exception ex)
        {
            // Say so on the line the reader is actually looking at. Leaving _current at its "(read to
            // see)" placeholder made a thrown read identical to a click that never landed — for the
            // visitor and for the E2E alike, which asserts on this element. A demo whose button appears
            // to do nothing when it fails teaches the wrong lesson twice over.
            _current = "read failed";
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Lock(OrientationLock to)
    {
        try
        {
            await orientation.LockAsync(to);
            // Read back AFTER claiming the lock, and claim it last: Read owns _status on its failure
            // path, so setting the lock's status first let a failed read-back overwrite it with
            // "Failed: …" — reporting a lock that had in fact succeeded as one that had not. The read
            // failing is still visible, on the line that belongs to it.
            await Read();
            _status = $"Locked to {to}";
        }
        catch (Exception ex)
        {
            _status = $"Lock rejected (needs fullscreen?): {ex.Message}";
        }
    }

    private async Task Unlock()
    {
        try
        {
            await orientation.UnlockAsync();
            _status = "Unlocked";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }
}
