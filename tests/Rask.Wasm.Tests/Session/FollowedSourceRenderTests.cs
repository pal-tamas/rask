using System.Text;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page that follows a source outside it: a render asked for while a dispatch renders
// is made inside it up to the budget, and one asked for past the budget is a render of its own after the dispatch,
// never dropped. A page that asks from every render is slowed, and its session goes on answering.
//
// Every point is chosen by the test from inside the page's own Render, and every request has been made before
// that Render returns, so nothing here depends on how fast the machine is.
[Collection("WasmSession")]
public sealed class FollowedSourceRenderTests : ResettingTestBase, IDisposable
{
    private readonly List<string> _painted = [];

    public FollowedSourceRenderTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _painted;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task A_request_inside_every_render_of_a_dispatch_leaves_the_last_value_painted_without_another_event(int lastWalk)
    {
        var app = new FollowingApp();
        var (session, click) = await Open(app);
        app.Arm(walk =>
        {
            if (walk <= lastWalk)
            {
                app.FollowFromOutside(walk + 1);
            }
        });

        await session.DispatchAsync(Click(click));
        await WaitFor.True(() => LastPaint().Contains($"value {lastWalk + 1}.", StringComparison.Ordinal), "the last value to be painted");

        Assert.Contains("clicks 1.", LastPaint(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_inside_a_render_is_one_more_render_however_many_threads_made_it()
    {
        var app = new FollowingApp();
        var (session, click) = await Open(app);
        app.Arm(walk =>
        {
            if (walk == 1)
            {
                app.FollowFromOutside(7, threads: 16);
            }
        });

        await session.DispatchAsync(Click(click));

        Assert.Equal(2, app.Walks);
        Assert.Contains("value 7.", LastPaint(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_asks_for_a_render_from_every_render_is_slowed_and_its_session_still_answers()
    {
        var app = new FollowingApp();
        var (session, click) = await Open(app);
        app.Arm(_ => app.StateHasChanged());

        await session.DispatchAsync(Click(click));
        await WaitFor.True(() => app.Walks >= AtFullSpeed, "the renders a runaway makes before it is slowed");
        var slowedAt = app.Walks;
        var started = TimeProvider.System.GetTimestamp();
        await WaitFor.True(() => app.Walks >= slowedAt + (2 * RendersARequestMayMake) + 1, "renders from three more requests");
        var theyTook = TimeProvider.System.GetElapsedTime(started);
        await session.DispatchAsync(Click(click));
        app.Arm(static _ => { });

        Assert.True(theyTook >= RenderPace.Pause, $"a runaway's further renders took {theyTook}, which is not slowed to one request every {RenderPace.Pause}.");
        Assert.Contains("clicks 2.", LastPaint(), StringComparison.Ordinal);
    }

    // The dispatch's three, and three for each request after it until the session is slowed.
    private const int RendersARequestMayMake = 3;
    private const int AtFullSpeed = RendersARequestMayMake * RenderPace.Streak;

    private string LastPaint()
    {
        lock (_painted)
        {
            return _painted.Count == 0 ? string.Empty : _painted[^1];
        }
    }

    private async Task<(WasmLiveSession Session, string Click)> Open(FollowingApp app)
    {
        var (session, _) = NewSession(_ => app, diffMode: DiffMode);
        var first = Encoding.UTF8.GetString(await session.InitialRenderAsync());
        var click = Regex.Match(
            first, "data-rask-on-click=..([^\"\\\\]+)", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        return (session, click);
    }

    private static byte[] Click(string id) => Utf8($$"""{"id":"{{id}}","type":"click"}""");
}

// A page that draws a value something outside it sets, and counts its clicks.
[SkipFactory]
internal sealed class FollowingApp : Component
{
    private Action<int>? _onWalk;
    private int _walks;
    private int _value;
    private int _clicks;

    /// <summary>How many times the page has rendered since <see cref="Arm" />.</summary>
    public int Walks => Volatile.Read(ref _walks);

    /// <summary>Counts renders from one, and runs <paramref name="onWalk" /> as each ends.</summary>
    public void Arm(Action<int> onWalk)
    {
        Volatile.Write(ref _walks, 0);
        Volatile.Write(ref _onWalk, onWalk);
    }

    /// <summary>
    ///     Sets the value and asks for a render from <paramref name="threads" /> threads at once, none of them
    ///     the caller's, and returns when all of them have.
    /// </summary>
    public void FollowFromOutside(int value, int threads = 4)
    {
        Volatile.Write(ref _value, value);
        var asking = Enumerable.Range(0, threads).Select(_ => new Thread(StateHasChanged)).ToArray();
        foreach (var thread in asking)
        {
            thread.Start();
        }

        foreach (var thread in asking)
        {
            thread.Join();
        }
    }

    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        Component drawn = Main[
            P[$"value {Volatile.Read(ref _value)}."],
            P[$"clicks {_clicks}."],
            Button.OnClick(() => _clicks++)["count"]
        ];

        // After the page has read what it draws: whatever changes now is the next render's to show.
        Volatile.Read(ref _onWalk)?.Invoke(Interlocked.Increment(ref _walks));
        return drawn;
    }
}
