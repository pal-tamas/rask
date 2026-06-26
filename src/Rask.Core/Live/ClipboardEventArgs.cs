using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the clipboard events (copy, cut, paste). The client reads `clipboardData` during
// the event (the only moment it's accessible without the async Clipboard API + a permission prompt)
// and ships the plain-text payload. For copy/cut, Text is the current selection; for paste, it's the
// text being pasted. Empty when the clipboard holds no text/plain data or access is blocked.
public sealed record ClipboardEventArgs(string Text)
{
    internal static ClipboardEventArgs FromJson(JsonElement p) => new(EventPayload.ReadString(p, "text"));
}
