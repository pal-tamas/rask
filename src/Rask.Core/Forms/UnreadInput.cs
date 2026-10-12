using System.Text.Json;

namespace Rask.Core.Forms;

// What the browser said of a field whose text it would not hand over. A number, date or time input holding
// text it cannot read reports its value as "", and the frame says so beside it: `"bad":"number"`, the input's
// type (rask-input.ts). Ambient while the value's handler starts, which is where a bind reads what arrived.
internal static class UnreadInput
{
    [ThreadStatic]
    private static string? _input;

    /// <summary>The type of the input that held text back, while the handler of its value starts.</summary>
    internal static string? Current => _input;

    /// <summary>The input type a frame names as holding text back, or null.</summary>
    internal static string? Of(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("bad"u8, out var bad) && bad.ValueKind == JsonValueKind.String
            ? bad.GetString()
            : null;

    /// <summary>Starts a value's handler knowing what the browser held back.</summary>
    internal static Task Run(Func<string, Task> handler, string value, string input)
    {
        _input = input;
        try
        {
            return handler(value);
        }
        finally
        {
            _input = null;
        }
    }

    /// <inheritdoc cref="Run(Func{string, Task}, string, string)" />
    internal static void Run(Action<string> handler, string value, string input)
    {
        _input = input;
        try
        {
            handler(value);
        }
        finally
        {
            _input = null;
        }
    }
}
