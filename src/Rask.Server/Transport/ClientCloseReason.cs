using System.Globalization;
using System.Net.WebSockets;
using System.Text;

namespace Rask.Server.Transport;

/// <summary>
///     The browser's close frame as the lifecycle log says it: its status, and the reason it gave — <c>auth-changed</c>,
///     <c>auth-refresh</c>, <c>session-expired</c> — which is WHY the tab dropped its connection.
/// </summary>
/// <remarks>
///     The reason is text the client chose, so it is bounded to <see cref="MaxReasonLength" /> characters and every
///     control character becomes <c>?</c>: no line break forges a second log line. It reaches the logger as the value
///     of a placeholder, never as its format.
/// </remarks>
internal static class ClientCloseReason
{
    /// <summary>The most of the client's reason a line keeps; a close frame allows 123 bytes, a reason needs a word.</summary>
    public const int MaxReasonLength = 64;

    /// <summary><c>close status 4000: auth-changed</c>, or <c>close status 1005</c> when the frame carried no status.</summary>
    public static string Describe(WebSocketCloseStatus? status, string? description)
    {
        var code = string.Create(CultureInfo.InvariantCulture, $"close status {(int?)status ?? 1005}");
        return string.IsNullOrWhiteSpace(description) ? code : $"{code}: {Bounded(description)}";
    }

    private static string Bounded(string description)
    {
        var length = Math.Min(description.Length, MaxReasonLength);
        if (length < description.Length && char.IsHighSurrogate(description[length - 1]))
        {
            length--; // never half a character
        }

        var text = new StringBuilder(length + 1);
        foreach (var c in description.AsSpan(0, length))
        {
            text.Append(char.IsControl(c) ? '?' : c);
        }

        if (description.Length > MaxReasonLength)
        {
            text.Append('…');
        }

        return text.ToString();
    }
}
