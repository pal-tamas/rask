using System.Text.Json;

namespace Rask.Core.Forms;

/// <summary>
///     What the browser last sent for the fields of one session, by their handler: written back as
///     <c>data-rask-heard</c> by the render that answers it, so the browser can tell the page's answer from a render
///     that knows nothing newer (#1245).
/// </summary>
/// <remarks>
///     <para>
///         A bound field that waits for the next action keeps what the reader typed while it has focus, against
///         every render — a reply to something else knows nothing about it (rask-morph.ts). The render that answers
///         the action the field went with is different: the page heard the field, and what it renders is the
///         model's value now. A chat box its submit empties is rendered empty — and since the last render had it
///         empty too, the diff says nothing, and the box kept the message. With the value it heard beside it, the
///         browser knows: a field that still holds exactly that takes the rendered value, focused or not, as
///         Livewire's does after the request.
///     </para>
///     <para>
///         A value is kept from its event until the next frame after a render wrote it back, so every render of
///         a slow handler, and of a batch, carries it; at most <see cref="Most" /> are kept.
///     </para>
/// </remarks>
internal sealed class HeardValues
{
    private const int Most = 64;

    private readonly Lock _gate = new();
    private Dictionary<string, Heard>? _heard;

    /// <summary>A frame from the browser begins: what a render has written back already goes.</summary>
    /// <remarks>Per frame, not per event: a batch may be rendered between its events, and its last render must still say it.</remarks>
    public void NextFrame()
    {
        lock (_gate)
        {
            if (_heard is { Count: > 0 } heard)
            {
                Forget(heard);
            }
        }
    }

    /// <summary>An event is dispatched: the value a <c>change</c> carries is kept for its field.</summary>
    /// <param name="handlerId">The handler the event is for.</param>
    /// <param name="payload">The event.</param>
    public void Hear(string handlerId, JsonElement payload)
    {
        lock (_gate)
        {
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("type", out var type) || !type.ValueEquals("change"u8)
                || !payload.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
            {
                return;
            }

            _heard ??= new Dictionary<string, Heard>(StringComparer.Ordinal);
            _heard[handlerId] = new Heard(value.GetString()!);
        }
    }

    /// <summary>What the browser sent for the field <paramref name="handlerId" /> commits, or null when nothing waits.</summary>
    /// <param name="handlerId">The field's handler, as this render registered it.</param>
    public string? Told(string handlerId)
    {
        lock (_gate)
        {
            if (_heard is not { Count: > 0 } heard || !heard.TryGetValue(handlerId, out var told))
            {
                return null;
            }

            told.Answered = true;
            return told.Value;
        }
    }

    private static void Forget(Dictionary<string, Heard> heard)
    {
        if (heard.Count >= Most)
        {
            heard.Clear();
            return;
        }

        foreach (var (id, told) in heard)
        {
            if (told.Answered)
            {
                heard.Remove(id);
            }
        }
    }

    private sealed class Heard(string value)
    {
        public string Value { get; } = value;

        public bool Answered { get; set; }
    }
}
