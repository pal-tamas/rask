using System.Text.Json;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>Reads a live Server-Sent Events stream the way the browser runtime does, each read with a deadline.</summary>
internal static class EventStream
{
    private const string Data = "data: ";

    /// <summary>The next event's line, past blank lines and heartbeat comments — or a failure rather than a hang.</summary>
    public static async Task<string> NextAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(LiveFrames.HangCeiling);
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            if (line.Length > 0 && !line.StartsWith(':'))
            {
                return line;
            }
        }
    }

    /// <summary>The frame that names the stream: <c>{"type":"stream",…}</c>.</summary>
    /// <remarks>
    ///     It is not always the first. A page repainted while no connection was attached — its scoped assets
    ///     changed, say — is sent that repaint as it attaches, ahead of the name, and the client holds such frames
    ///     until the name arrives. A test that took the first event for the name posted with none, and was told 409.
    /// </remarks>
    public static async Task<string> OpeningFrameAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await NextAsync(reader);
            if (line.StartsWith(Data + "{\"type\":\"stream\"", StringComparison.Ordinal))
            {
                return line;
            }
        }
    }

    /// <summary>The generation the stream names, as a POST's <c>Rask-Stream</c> header carries it.</summary>
    public static async Task<string> GenerationAsync(StreamReader reader)
    {
        using var opening = JsonDocument.Parse((await OpeningFrameAsync(reader))[Data.Length..]);
        return opening.RootElement.GetProperty("generation").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
