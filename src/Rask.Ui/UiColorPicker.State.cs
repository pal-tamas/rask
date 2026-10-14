using System.Runtime.InteropServices;
using Rask.Core.Forms;

namespace Rask;

// The colour the page holds between renders, and how a colour the browser took reaches it.
public sealed partial class UiColorPicker
{
    // What the page holds, which is what the picker is drawn with: everything the reader does before a value is
    // owed is the browser's (`data-rask-color`, rask-color.ts).
    private Pick _kept = Pick.None;

    // Text that was no colour, which a picker bound in a form keeps and refuses.
    private string? _refused;

    // The stored text the colour above was last read from or written as. Any other text is the page's news.
    private string? _seen;

    private bool _read;

    // The colour of a picker with neither Bind nor OnChange, once it has changed.
    private string? _own;

    private bool _ownSet;

    // The binding as this render resolved it: what a handler commits through.
    private (ExpressionAccessor.Accessor? Accessor, EditContext? Context) _bound;

    private bool Keeps => Bind is null && !OnChange.HasValue;

    private bool Refuses => _bound is { Accessor: not null, Context: not null };

    // Called once a render; a bound picker registers its validator here.
    private void Read()
    {
        var stored = Stored();
        if (_read && string.Equals(stored, _seen, StringComparison.Ordinal))
        {
            return;
        }

        (_read, _seen, _refused) = (true, stored, null);
        _kept = UiColorValue.TryParse(stored, out var color) ? new Pick(color, Empty: false) : Pick.None;
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

    // What the browser announced through the picker's one field: a colour in the picker's format, nothing (the
    // colour cleared), or text that is no colour, which only a picker bound in a form is sent (RefuseAsync).
    private Task ChangedAsync(string text)
    {
        if (Off)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(text) || UiColorValue.TryParse(text, out _))
        {
            _refused = null;
            return KeepAsync(UiColorValue.TryParse(text, out var color) ? new Pick(color, Empty: false) : Pick.None);
        }

        if (!Refuses)
        {
            return Task.CompletedTask;
        }

        _refused = text;
        return RefuseAsync();
    }

    // The dropper's colour, with the alpha the picker had: the page's at once.
    private Task DroppedAsync(string? hex) =>
        UiColorValue.TryParse(hex, out var color) ? KeepAsync(new Pick(color with { Alpha = _kept.Color.Alpha }, Empty: false)) : Task.CompletedTask;

    private Task KeepAsync(Pick pick)
    {
        if (_kept == pick)
        {
            return Task.CompletedTask;
        }

        _kept = pick;

        return StoreAsync(_kept.Text(Written));
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
