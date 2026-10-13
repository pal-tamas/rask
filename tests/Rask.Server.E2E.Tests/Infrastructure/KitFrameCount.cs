using System.Globalization;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>What one gesture put on the socket: frames the page sent, and frames the server answered with.</summary>
internal readonly record struct KitFrames(int Sent, int Received);

/// <summary>A gesture and what it cost, or why it could not be made.</summary>
internal sealed record KitCount(string Gesture, KitFrames Frames, string? Error);

/// <summary>
///     Puts a component's page through its gestures, each on a page loaded afresh, and counts the frames of each.
/// </summary>
/// <remarks>
///     Time is the page's own: after a gesture the page's clock is run on by two seconds, so a debounce, a hover
///     delay or a timer that would send something has fired, and then the socket is watched until it has been
///     quiet for a while in real time, which is what a round trip takes.
/// </remarks>
internal static class KitFrameCount
{
    private const int QuietMs = 300;

    public static async Task<IReadOnlyList<KitCount>> OfAsync<TPage>(
        PlaywrightFixture playwright, IReadOnlyList<KitGesture> gestures)
        where TPage : Rask.Core.Component
    {
        var frames = new Counter();
        await using var host = await LiveServerHost.StartAsync<TPage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await context.NewPageAsync();
        await PageClock.Install(page);
        await page.AddInitScriptAsync(
            """
            window.WebSocket = class extends WebSocket {
                constructor(...args) { super(...args); this.addEventListener('open', () => { window.__raskOpen = true; }); }
            };
            """);
        page.WebSocket += (_, socket) =>
        {
            socket.FrameSent += (_, _) => frames.Sent();
            socket.FrameReceived += (_, _) => frames.Received();
        };
        page.SetDefaultTimeout(15_000);
        var counted = new List<KitCount>();
        foreach (var gesture in gestures)
        {
            try
            {
                counted.Add(new KitCount(gesture.Name, await OneAsync(page, host.BaseUrl, frames, gesture, typeof(TPage).Name), null));
            }
            catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
            {
                // A gesture that could not be made says so beside the others, which are still counted.
                counted.Add(new KitCount(gesture.Name, default, failure.Message.Split('\n')[0]));
                await page.Mouse.UpAsync();
            }
        }

        return counted;
    }

    private static async Task<KitFrames> OneAsync(IPage page, string baseUrl, Counter frames, KitGesture gesture, string name)
    {
        await page.GotoAsync(baseUrl + "/");
        await ReadyAsync(page);
        await page.Mouse.MoveAsync(KitGestures.AwayX, KitGestures.AwayY);
        if (Environment.GetEnvironmentVariable("RASK_KIT_FRAMES_REPORT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".html"), await page.ContentAsync());
        }

        if (gesture.Arrange is { } arrange)
        {
            await arrange(page);
        }

        await SettleAsync(page, frames);
        var before = frames.Now;
        await gesture.Do(page);
        await SettleAsync(page, frames);
        var after = frames.Now;
        await page.Mouse.UpAsync();
        return new KitFrames(after.Sent - before.Sent, after.Received - before.Received);
    }

    /// <summary>Writes what was counted where <c>RASK_KIT_FRAMES_REPORT</c> points, one file a component.</summary>
    public static void Report(string component, IReadOnlyList<KitCount> counted)
    {
        if (Environment.GetEnvironmentVariable("RASK_KIT_FRAMES_REPORT") is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllLines(
            Path.Combine(directory, component.Replace(' ', '-') + ".tsv"),
            counted.Select(row => string.Join('\t', component, row.Gesture,
                row.Frames.Sent.ToString(CultureInfo.InvariantCulture), row.Frames.Received.ToString(CultureInfo.InvariantCulture), row.Error)));
    }

    // The socket is open, and the hooks have run when the page asked for them.
    private static async Task ReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => window.__raskOpen === true");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('script[data-rask-hooks]') || !(window.__raskHookSeam && window.__raskHookSeam.missed)");
    }

    private static async Task SettleAsync(IPage page, Counter frames)
    {
        await page.Clock.FastForwardAsync(2000);
        var last = frames.Now;
        var quietSince = Environment.TickCount64;
        var started = quietSince;
        while (Environment.TickCount64 - quietSince < QuietMs && Environment.TickCount64 - started < 8000)
        {
            await Task.Delay(50);
            var now = frames.Now;
            if (now != last)
            {
                last = now;
                quietSince = Environment.TickCount64;
                // What the answer started — a timer, a transition's end — gets its time too.
                await page.Clock.FastForwardAsync(500);
            }
        }
    }

    private sealed class Counter
    {
        private int _sent;
        private int _received;

        public KitFrames Now => new(Volatile.Read(ref _sent), Volatile.Read(ref _received));

        public void Sent() => Interlocked.Increment(ref _sent);

        public void Received() => Interlocked.Increment(ref _received);
    }
}
