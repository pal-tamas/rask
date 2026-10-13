using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask;

/// <summary>
///     The pause a field counts before the page hears what was typed into it: the one reading of a
///     <c>Debounce</c> step for every field of the kit that asks a store — the select's search field and its
///     combobox input, the pillbox's two, the autocomplete's.
/// </summary>
/// <remarks>
///     The browser counts the pause (<c>data-rask-debounce</c>) and sends the handler named beside it
///     (<c>data-rask-on-pause</c>, rask-input.ts) the field's text once typing has stopped that long. Nothing
///     else sends it: a row pressed during the pause is a row of the list as drawn.
/// </remarks>
internal static class UiSearchPause
{
    /// <summary>The pause in milliseconds, or 0 when every keystroke is heard.</summary>
    /// <param name="debounce">The field's <c>Debounce</c> step.</param>
    /// <remarks>With no page behind the render there is no handler to name, and nothing to wait for.</remarks>
    internal static int Of(TimeSpan? debounce) =>
        debounce is null || LiveRenderContext.CurrentSync is null ? 0 : new BindTiming { Debounce = debounce }.Pause;

    /// <summary>The same, for a field whose pause only its listener hears: nobody listening, no pause.</summary>
    /// <param name="heard">The field's <c>OnInput</c>.</param>
    /// <param name="debounce">The field's <c>Debounce</c> step.</param>
    internal static int Of(Callback<string> heard, TimeSpan? debounce) => heard.HasValue ? Of(debounce) : 0;

    /// <summary><paramref name="marks" /> with what asks the runtime for the pause over them, when there is one.</summary>
    /// <param name="marks">The field's own marks.</param>
    /// <param name="pause">The pause in milliseconds, from <see cref="Of(TimeSpan?)" />.</param>
    /// <param name="atPause">What hears the field's text at the pause.</param>
    internal static Dictionary<string, string?> Over(Dictionary<string, string?> marks, int pause, Func<string, Task> atPause)
    {
        if (pause == 0 || LiveRenderContext.CurrentSync is not { } live)
        {
            return marks;
        }

        return new Dictionary<string, string?>(marks, StringComparer.Ordinal)
        {
            ["data-rask-debounce"] = pause.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["data-rask-on-pause"] = live.RegisterHandler(atPause),
        };
    }
}
