using System.Globalization;
using Rask.Core;

namespace Rask;

// What the keyboard and the pointer do, as recorded on Flux's page: the area steps by 2 and by 10 with Shift,
// a slider by 1 and by 10, and under a pointer every change is the page's at once. On the sheet — the popover
// below `sm` — a change waits for Select; a swatch does not wait, it is taken and the sheet closes.
public sealed partial class UiColorPicker
{
    private bool _open;

    // Whether the popover is Flux's sheet: the browser says so through the probe in the dialog (Measured).
    private bool _sheet;

    // The palette's tab stop once the arrows have moved it. Unset, it is the picked swatch, or the first.
    private int? _roving;

    // What assistive tech was last told: "Red selected".
    private string _said = string.Empty;

    // The dialog's own toggle event: the browser reports every way it opened or closed — the button's
    // command, Escape, a press outside, a button of the sheet — and none of them is a handler here.
    private void Toggled(ToggleEvent e)
    {
        var open = string.Equals(e.NewState, "open", StringComparison.Ordinal);
        if (open != _open)
        {
            Opened(open);
        }
    }

    // Closed, the popover shows what the page holds again: what nobody selected on the sheet is gone.
    private void Opened(bool open)
    {
        (_open, _roving) = (open, null);
        if (!open)
        {
            _shown = _kept;
        }
    }

    // ArrowDown opens the popover, with Alt or without: the dialog follows what the render says.
    private void OpeningKey(KeyboardEvent e)
    {
        if (string.Equals(e.Key, Keys.ArrowDown, StringComparison.Ordinal) && !Off && !_open)
        {
            Opened(open: true);
        }
    }

    // The probe's box as the browser measured it: two wide below `sm`, one from there up.
    private void Measured(string size) => _sheet = string.Equals(size, SheetBox[0], StringComparison.Ordinal);

    // ----- The area ---------------------------------------------------------------------------------------

    private Task AreaKeyAsync(KeyboardEvent e)
    {
        var step = e.ShiftKey ? 10 : 2;
        var (saturation, brightness) = (_shown.Color.Saturation, _shown.Color.Brightness);
        (double Saturation, double Brightness)? moved = e.Key switch
        {
            Keys.ArrowRight => (saturation + step, brightness),
            Keys.ArrowLeft => (saturation - step, brightness),
            Keys.ArrowUp => (saturation, brightness + step),
            Keys.ArrowDown => (saturation, brightness - step),
            Keys.PageUp => (saturation, 100),
            Keys.PageDown => (saturation, 0),
            Keys.Home => (0, brightness),
            Keys.End => (100, brightness),
            _ => null,
        };

        return moved is { } to && !Off
            ? MoveAsync(_shown.Color with { Saturation = Math.Clamp(to.Saturation, 0, 100), Brightness = Math.Clamp(to.Brightness, 0, 100) })
            : Task.CompletedTask;
    }

    // Where the pointer is on the area, as the runtime announces it: across, then down, each 0 to 1.
    private Task AreaDraggedAsync(string at)
    {
        var parts = at.Split(' ');
        if (Off || parts.Length != 2 || !TryFraction(parts[0], out var across) || !TryFraction(parts[1], out var down))
        {
            return Task.CompletedTask;
        }

        return MoveAsync(_shown.Color with { Saturation = across * 100, Brightness = (1 - down) * 100 });
    }

    // ----- The sliders ------------------------------------------------------------------------------------

    // Where the pointer is along a track, 0 to 1. A slider moves in whole steps, as Flux's does.
    private Task ChannelDraggedAsync(Ui.ColorPickerChannel channel, string at) =>
        !Off && TryFraction(at, out var along) ? SetChannelAsync(channel, (int)Math.Floor((along * Max(channel)) + 0.5)) : Task.CompletedTask;

    // An arrow steps by one, and by ten with Shift — Flux's big step, which a range input does not have, so the
    // runtime keeps the arrows from the input and they are taken here.
    private Task ChannelKeyAsync(Ui.ColorPickerChannel channel, KeyboardEvent e)
    {
        var direction = e.Key switch
        {
            Keys.ArrowRight or Keys.ArrowUp => 1,
            Keys.ArrowLeft or Keys.ArrowDown => -1,
            _ => 0,
        };

        if (direction == 0 || Off)
        {
            return Task.CompletedTask;
        }

        var step = e.ShiftKey ? 10 : 1;

        return SetChannelAsync(channel, Math.Clamp(ChannelValue(channel) + (direction * step), 0, Max(channel)));
    }

    // The range input's own change: Home, End and the page keys are the browser's — a page key is a tenth of
    // the track, as on Flux.
    private Task ChannelTypedAsync(Ui.ColorPickerChannel channel, string text) =>
        !Off && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? SetChannelAsync(channel, Math.Clamp(value, 0, Max(channel)))
            : Task.CompletedTask;

    private Task SetChannelAsync(Ui.ColorPickerChannel channel, int value)
    {
        if (value == ChannelValue(channel))
        {
            return Task.CompletedTask;
        }

        return MoveAsync(channel == Ui.ColorPickerChannel.Alpha ? _shown.Color with { Alpha = value / 100d } : _shown.Color with { Hue = value });
    }

    private static int Max(Ui.ColorPickerChannel channel) => channel == Ui.ColorPickerChannel.Alpha ? 100 : 360;

    private static bool TryFraction(string text, out double fraction) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out fraction) && fraction is >= 0 and <= 1;

    // ----- The dropper ------------------------------------------------------------------------------------

    private Task DroppedAsync(string? hex) =>
        UiColorValue.TryParse(hex, out var color) ? MoveAsync(color with { Alpha = _shown.Color.Alpha }) : Task.CompletedTask;

    // ----- The palette ------------------------------------------------------------------------------------

    private int TabStop(UiColorPalette palette)
    {
        var picked = _shown.Empty ? -1 : palette.IndexOf(Matched(_shown));

        return _roving is { } roving && roving < palette.Swatches.Count ? roving : Math.Max(picked, 0);
    }

    // A pick leaves the tab stop where it was, as Flux's does: it follows the colour again when the list opens.
    // It is the page's at once, on the sheet too, and the sheet closes on it.
    private Task PickedAsync(UiColorPalette palette, int index)
    {
        var swatch = palette.Swatches[index];
        if (swatch.Color is not { } color || Off)
        {
            return Task.CompletedTask;
        }

        _roving = TabStop(palette);
        _said = RaskStrings.Get(RaskString.ColorPickerSelected, "{0} selected", swatch.Named);
        _shown = new Pick(color, Empty: false);
        if (_sheet && _open)
        {
            _open = false;
        }

        return KeepAsync();
    }

    // The arrows move focus and stop at the edges; Home and End go to either end; Enter and Space pick.
    private Task SwatchKeyAsync(UiColorPalette palette, int index, KeyboardEvent e)
    {
        var last = palette.Swatches.Count - 1;
        var target = e.Key switch
        {
            Keys.ArrowRight => index + 1,
            Keys.ArrowLeft => index - 1,
            Keys.ArrowDown => index + UiColorPalette.Columns,
            Keys.ArrowUp => index - UiColorPalette.Columns,
            Keys.Home => 0,
            Keys.End => last,
            _ => index,
        };
        if (e.Key is Keys.Enter or " ")
        {
            return PickedAsync(palette, index);
        }

        if (target != index && target >= 0 && target <= last)
        {
            _roving = target;
            UiReach.Send(() => _swatchRefs[target].Focus());
        }

        return Task.CompletedTask;
    }

    // The swatch a colour is told from, by what the picker writes: a colour between two of an 8-bit format's
    // steps is the swatch it is written as, which is how Flux's label reads.
    private string Matched(Pick pick) =>
        UiColorValue.TryParse(pick.Text(Written), out var written) ? written.Hex : pick.Color.Hex;
}
