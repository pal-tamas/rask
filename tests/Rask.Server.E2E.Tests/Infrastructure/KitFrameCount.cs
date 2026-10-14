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
///     Nothing counted here depends on how fast the machine is. While a gesture is made the page's clock stands
///     still, and the gesture itself gives the page two animation frames after each key, press and step of the
///     pointer and waits for the server's answer to it (<see cref="KitGestures.BeatAsync" />): a field that sends
///     once a frame sends once a key, however many keys a fast machine fits into a frame. Afterwards the clock
///     is run on by two seconds, so a debounce, a hover delay or a timer that would send something has fired,
///     and the count is read once the server has answered everything and the socket has been quiet for a while.
/// </remarks>
internal static class KitFrameCount
{
    private const int QuietMs = 300;

    // A wait here is for a condition — a part to press, an answer — and this is only how long a broken one hangs.
    private const int StuckMs = 60_000;

    public static async Task<IReadOnlyList<KitCount>> OfAsync<TPage>(
        PlaywrightFixture playwright, IReadOnlyList<KitGesture> gestures)
        where TPage : Rask.Core.Component
    {
        await using var host = await LiveServerHost.StartAsync<TPage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await context.NewPageAsync();
        await PageClock.Install(page);
        await SlowMachine.ApplyAsync(page);
        await KitSocket.Install(page);
        page.SetDefaultTimeout(StuckMs);
        var counted = new List<KitCount>();
        foreach (var gesture in gestures)
        {
            try
            {
                counted.Add(new KitCount(gesture.Name, await OneAsync(page, host.BaseUrl, gesture, typeof(TPage).Name, counted.Count + 1), null));
            }
            catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
            {
                // A gesture that could not be made says so beside the others, which are still counted.
                counted.Add(new KitCount(gesture.Name, default, failure.Message.Split('\n')[0]));
                await page.Mouse.UpAsync();
                await page.Clock.ResumeAsync();
            }
        }

        return counted;
    }

    private static async Task<KitFrames> OneAsync(IPage page, string baseUrl, KitGesture gesture, string name, int nth)
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

        await SettleAsync(page);
        // From here the page's time passes only when the gesture, or the settling after it, says so.
        await PageClock.StopAsync(page, nth);
        var before = await KitSocket.CountAsync(page);
        await gesture.Do(page);
        await SettleAsync(page);
        var after = await KitSocket.CountAsync(page);
        await page.Mouse.UpAsync();
        await page.Clock.ResumeAsync();
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
        await KitSocket.OpenAsync(page);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('script[data-rask-hooks]') || !(window.__raskHookSeam && window.__raskHookSeam.missed)");
    }

    // Everything asked is answered, the page's timers have had their time, and nothing more comes of either.
    private static async Task SettleAsync(IPage page)
    {
        await KitSocket.AnsweredAsync(page);
        await page.Clock.FastForwardAsync(2000);
        var last = await KitSocket.CountAsync(page);
        var quietSince = Environment.TickCount64;
        var started = quietSince;
        while (Environment.TickCount64 - quietSince < QuietMs)
        {
            if (Environment.TickCount64 - started > StuckMs)
            {
                throw new TimeoutException("The socket was never quiet: something sends for as long as the page is up.");
            }

            await Task.Delay(50);
            await KitSocket.AnsweredAsync(page);
            var now = await KitSocket.CountAsync(page);
            if (now != last)
            {
                last = now;
                quietSince = Environment.TickCount64;
                // What the answer started — a timer, a transition's end — gets its time too.
                await page.Clock.FastForwardAsync(500);
            }
        }
    }
}
