using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>Every frame a page's live socket carried, in order: what a journey asserts a reply WAS.</summary>
internal sealed class SocketFrames
{
    private readonly ConcurrentQueue<(bool Sent, string Text)> _frames = new();
    private readonly SocketHello _hello;

    private SocketFrames(IPage page)
    {
        page.WebSocket += (_, socket) =>
        {
            socket.FrameSent += (_, frame) => _frames.Enqueue((true, frame.Text ?? string.Empty));
            socket.FrameReceived += (_, frame) => _frames.Enqueue((false, frame.Text ?? string.Empty));
        };
        // Watched after the frames, so the hello is among them once it is said to have gone.
        _hello = SocketHello.Watch(page);
    }

    /// <summary>Listens to every socket <paramref name="page" /> opens. Call before the page loads.</summary>
    public static SocketFrames Of(IPage page) => new(page);

    /// <summary>Completes once the socket is up and has said hello (<see cref="SocketHello" />).</summary>
    public Task OpenedAsync() => _hello.SentAsync();

    /// <summary>The replies that carried the whole document instead of a diff.</summary>
    public IReadOnlyList<string> FullPages =>
        [.. _frames.Where(frame => !frame.Sent && frame.Text.Contains("\"html\":\"<!DOCTYPE", StringComparison.Ordinal)).Select(frame => Cut(frame.Text))];

    /// <summary>The paths of the parents whose children a reply sent as markup, one <c>MorphSubtree</c> each.</summary>
    public IReadOnlyList<string> Morphs =>
    [
        .. _frames.Where(frame => !frame.Sent)
            .SelectMany(frame => System.Text.RegularExpressions.Regex.Matches(
                frame.Text, "\\[8,\\[([0-9,]*)\\],", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Select(match => match.Groups[1].Value),
    ];

    /// <summary>How many frames the page has put on the socket so far.</summary>
    public int Sent => _frames.Count(frame => frame.Sent);

    /// <summary>The largest reply so far, in bytes.</summary>
    public int LargestReply => _frames.Where(frame => !frame.Sent).Select(frame => frame.Text.Length).DefaultIfEmpty().Max();

    /// <summary>Everything the server sent, whole: for what must not be in any of it.</summary>
    public string Replies() => string.Join(Environment.NewLine, _frames.Where(frame => !frame.Sent).Select(frame => frame.Text));

    /// <summary>The whole exchange, one line a frame: for a failure message, or a trace while writing a journey.</summary>
    public string Trace() =>
        string.Join(Environment.NewLine, _frames.Select(frame => (frame.Sent ? "> " : "< ") + frame.Text.Length + " " + Cut(frame.Text)));

    /// <summary>Forgets what was carried so far: the load is not what a journey is about.</summary>
    public void Clear() => _frames.Clear();

    private static string Cut(string text) => text.Length <= 160 ? text : text[..160] + "…";
}
