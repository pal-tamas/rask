using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>Every frame a page's live socket carried, in order: what a journey asserts a reply WAS.</summary>
internal sealed class SocketFrames
{
    private readonly ConcurrentQueue<(bool Sent, string Text)> _frames = new();

    /// <summary>Listens to every socket <paramref name="page" /> opens. Call before the page loads.</summary>
    public static SocketFrames Of(IPage page)
    {
        var frames = new SocketFrames();
        page.WebSocket += (_, socket) =>
        {
            socket.FrameSent += (_, frame) => frames._frames.Enqueue((true, frame.Text ?? string.Empty));
            socket.FrameReceived += (_, frame) => frames._frames.Enqueue((false, frame.Text ?? string.Empty));
        };

        return frames;
    }

    /// <summary>The replies that carried the whole document instead of a diff.</summary>
    public IReadOnlyList<string> FullPages =>
        [.. _frames.Where(frame => !frame.Sent && frame.Text.Contains("\"html\":\"<!DOCTYPE", StringComparison.Ordinal)).Select(frame => Cut(frame.Text))];

    /// <summary>The largest reply so far, in bytes.</summary>
    public int LargestReply => _frames.Where(frame => !frame.Sent).Select(frame => frame.Text.Length).DefaultIfEmpty().Max();

    /// <summary>The whole exchange, one line a frame: for a failure message, or a trace while writing a journey.</summary>
    public string Trace() =>
        string.Join(Environment.NewLine, _frames.Select(frame => (frame.Sent ? "> " : "< ") + frame.Text.Length + " " + Cut(frame.Text)));

    /// <summary>Forgets what was carried so far: the load is not what a journey is about.</summary>
    public void Clear() => _frames.Clear();

    private static string Cut(string text) => text.Length <= 160 ? text : text[..160] + "…";
}
