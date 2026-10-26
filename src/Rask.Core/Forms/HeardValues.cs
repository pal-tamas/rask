using System.Globalization;
using System.Text.Json;

namespace Rask.Core.Forms;

/// <summary>
///     What the browser last sent for each bound field of one session, by its handler: written beside the field as
///     <c>data-rask-heard="&lt;count&gt;:&lt;text&gt;"</c> so the browser can tell the page's answer from a render that
///     knows nothing newer (#1245).
/// </summary>
/// <remarks>
///     <para>
///         A bound field that waits for the next action keeps what the reader typed while it has focus, against
///         every render — a reply to something else knows nothing about it (rask-morph.ts). The render that answers
///         the action the field went with is different: the page heard the field, and what it renders is the
///         model's value now. A chat box its submit empties is rendered empty — and since the last render had it
///         empty too, the diff says nothing, and the box kept the message. With what it heard beside it, the browser
///         knows: a field that still holds exactly that takes the rendered value, focused or not, as Livewire's
///         does after the request.
///     </para>
///     <para>
///         The count moves with every <c>change</c> heard, so the same message sent twice is a new word each time,
///         and the browser acts on a word once. Nothing takes it back out: removing it would be a frame of its own.
///         At most <see cref="Most" /> fields are kept.
///     </para>
///     <para>
///         A secret field gets the count alone (<c>data-rask-heard="3"</c>), and the browser then only ever CLEARS it,
///         when the page renders it empty: no text of a password goes into a page, a frame or an answer here.
///     </para>
/// </remarks>
internal sealed class HeardValues
{
    private const int Most = 64;

    private readonly Lock _gate = new();
    private Dictionary<string, (int Count, string Text)>? _heard;

    /// <summary>An event is dispatched: the value a <c>change</c> carries is kept for its field.</summary>
    /// <param name="handlerId">The handler the event is for.</param>
    /// <param name="payload">The event.</param>
    public void Hear(string handlerId, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("type", out var type) || !type.ValueEquals("change"u8)
            || !payload.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        lock (_gate)
        {
            _heard ??= new Dictionary<string, (int, string)>(StringComparer.Ordinal);
            if (_heard.Count >= Most && !_heard.ContainsKey(handlerId))
            {
                _heard.Clear();
            }

            _heard[handlerId] = (_heard.TryGetValue(handlerId, out var before) ? before.Count + 1 : 1, value.GetString()!);
        }
    }

    /// <summary>What the browser last sent for the field <paramref name="handlerId" /> commits, or null.</summary>
    /// <param name="handlerId">The field's handler, as this render registered it.</param>
    /// <param name="secret">The field holds a secret: only the count is written, never the text.</param>
    public string? Told(string handlerId, bool secret = false)
    {
        lock (_gate)
        {
            if (_heard is not { Count: > 0 } heard || !heard.TryGetValue(handlerId, out var told))
            {
                return null;
            }

            return secret
                ? told.Count.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{told.Count}:{told.Text}");
        }
    }
}
