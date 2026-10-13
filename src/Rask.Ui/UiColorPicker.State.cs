using System.Runtime.InteropServices;
using Rask.Core.Forms;

namespace Rask;

// The colour the picker holds between renders, and how it reaches the page.
public sealed partial class UiColorPicker
{
    // What the page holds, which is what the trigger shows.
    private Pick _kept = Pick.None;

    // What the popover shows. The same as the page holds, but on the sheet between a choice and its Select.
    private Pick _shown = Pick.None;

    // The stored text the colour above was last read from or written as. Any other text is the page's news.
    private string? _seen;

    private bool _read;

    // The colour of a picker with neither Bind nor OnChange, once it has changed.
    private string? _own;

    private bool _ownSet;

    // The binding as this render resolved it: what a handler commits through.
    private (ExpressionAccessor.Accessor? Accessor, EditContext? Context) _bound;

    private bool Keeps => Bind is null && !OnChange.HasValue;

    // Called once a render; a bound picker registers its validator here.
    private void Read()
    {
        var stored = Stored();
        if (_read && string.Equals(stored, _seen, StringComparison.Ordinal))
        {
            return;
        }

        (_read, _seen) = (true, stored);
        _kept = _shown = UiColorValue.TryParse(stored, out var color) ? new Pick(color, Empty: false) : Pick.None;
    }

    private string? Stored()
    {
        if (!Keeps)
        {
            var (accessor, context, current) = UiFormCommit.Resolve(this);
            _bound = (accessor, context);

            return current;
        }

        return _ownSet ? _own : Value;
    }

    /// <summary>Takes a colour from the area, a slider or the dropper: the palette's tab stop follows the colour again.</summary>
    private Task MoveAsync(UiColorValue color)
    {
        _roving = null;

        return ChooseAsync(new Pick(color, Empty: false));
    }

    // A choice made in the popover. Under a pointer it is the page's at once; on the sheet it waits for Select.
    private Task ChooseAsync(Pick pick)
    {
        _shown = pick;

        return _sheet && _open ? Task.CompletedTask : KeepAsync();
    }

    // The page takes what the popover shows.
    private Task KeepAsync()
    {
        if (_kept == _shown)
        {
            return Task.CompletedTask;
        }

        _kept = _shown;

        return StoreAsync(_kept.Text(Written));
    }

    // A choice made on the trigger, which is never held back.
    private Task TakeAsync(Pick pick)
    {
        (_shown, _roving) = (pick, null);

        return KeepAsync();
    }

    private Task StoreAsync(string text)
    {
        _seen = text;
        if (Keeps)
        {
            (_own, _ownSet) = (text, true);

            return Task.CompletedTask;
        }

        return UiFormCommit.CommitAsync(this, _bound.Accessor, _bound.Context, text);
    }

    // Text in a field that is no colour. Flux puts the last colour back and says nothing; a form must not save
    // a value its reader can no longer see, so a bound picker says so under its field and keeps the text.
    private Task RefuseAsync() =>
        UiFormCommit.UnreadAsync(this, _bound.Accessor, _bound.Context, _kept.Text(Written), RaskString.ValidationColor);

    private bool Refuses => _bound is { Accessor: not null, Context: not null };

    /// <summary>A colour, or none at all: an empty picker stands at black until the first move makes a colour.</summary>
    /// <param name="Color">Held as hue, saturation and brightness: the text the page stores forgets the hue of black.</param>
    /// <param name="Empty">No colour at all.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Pick(UiColorValue Color, bool Empty)
    {
        internal static readonly Pick None = new(UiColorValue.Black, Empty: true);

        /// <summary>The colour as an input shows it: in the picker's format, whatever CSS it was given in.</summary>
        internal string Text(Ui.ColorPickerFormat format) => Empty ? string.Empty : Color.Format(format);

        /// <summary>The colour as a preview paints it.</summary>
        internal string Css => Empty ? "transparent" : Color.Css;
    }
}
