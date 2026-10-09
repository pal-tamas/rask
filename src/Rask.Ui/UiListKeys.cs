using Rask.Core;

namespace Rask;

/// <summary>
///     The keys a text input over a list acts on, as the runtime's <c>data-rask-keys</c> reads them: it sends
///     the input's key handler no other.
/// </summary>
/// <remarks>
///     Every key used to be sent. A letter typed into <c>Ui.Autocomplete</c>, <c>Ui.Select</c>'s combobox or
///     <c>Ui.Pillbox</c>'s input was then two round trips and two renders — the key, which changed nothing, and
///     the <c>input</c> — and on a page that renders slower than a key they queued up behind the reader.
/// </remarks>
internal static class UiListKeys
{
    /// <summary>The arrows walk the list, Enter picks, Escape and Tab close it.</summary>
    internal const string Text = $"{Keys.ArrowDown} {Keys.ArrowUp} {Keys.Enter} {Keys.Escape} {Keys.Tab}";

    /// <summary>The same, and Backspace: in an empty pillbox input it takes the last pill off.</summary>
    internal const string Pills = $"{Text} {Keys.Backspace}";

    /// <summary>
    ///     The keys that close a pillbox and empty its input — Flux's <c>clear="close"</c> — as the runtime's
    ///     <c>data-rask-clear-keys</c> reads them: emptied in the browser, at the key.
    /// </summary>
    internal const string PillsClear = $"{Keys.Escape} {Keys.Tab}";
}
