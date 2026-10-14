using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>One thing a reader does to a kit component: what is counted is every frame it puts on the socket.</summary>
/// <param name="Name">What the reader does, in a few words.</param>
/// <param name="Do">The gesture itself.</param>
/// <param name="Arrange">What comes before it and is not counted: opening the popup the gesture happens in.</param>
internal sealed record KitGesture(string Name, Func<IPage, Task> Do, Func<IPage, Task>? Arrange = null);

/// <summary>The gestures every component is put through, and the few steps they are made of.</summary>
/// <remarks>
///     A gesture is made of steps — a key, a press, the pointer moved on a little — and after each the page is
///     given a <see cref="BeatAsync">beat</see>. The page's clock stands still meanwhile (<see cref="KitFrameCount" />),
///     so a step costs what it costs a reader, who is slower than a frame: never less because a fast machine made
///     two steps inside one frame, never more because a slow one let a timer run out between them.
/// </remarks>
internal static class KitGestures
{
    // One animation frame of the page's time, and a little: what is sent once a frame has been sent.
    private const int FrameMs = 20;

    private const int DragSteps = 12;

    /// <summary>Where a component under test is drawn.</summary>
    public const string Stage = "#stage";

    /// <summary>A place on the page no component is drawn at: where the pointer rests and an outside press lands.</summary>
    public const int AwayX = 1265;

    /// <inheritdoc cref="AwayX" />
    public const int AwayY = 785;

    // Every part a pointer or the keyboard can act on, as boxes in the viewport, in document order.
    private const string Parts = """
        root => {
            const scope = document.querySelector(root);
            if (!scope) { return []; }
            const wanted = 'a,button,input,select,textarea,summary,label,th,[role],[tabindex],[popover],[data-rask-drag],[data-ui-chart-hover],svg';
            const seen = [];
            for (const el of scope.querySelectorAll(wanted)) {
                const box = el.getBoundingClientRect();
                if (box.width < 2 || box.height < 2 || box.bottom < 0 || box.top > innerHeight || box.right < 0 || box.left > innerWidth) { continue; }
                if (getComputedStyle(el).visibility === 'hidden') { continue; }
                seen.push([box.left + box.width / 2, box.top + box.height / 2]);
                if (seen.length === 40) { break; }
            }
            return seen;
        }
        """;

    /// <summary>The pointer enters, crosses and leaves every part inside <paramref name="root" />, then leaves it.</summary>
    public static KitGesture Pointer(string root = Stage, Func<IPage, Task>? arrange = null, string name = "pointer over every part") =>
        new(name, page => SweepAsync(page, root), arrange);

    /// <summary>Tab walks through the parts inside <paramref name="root" /> and back out: focus and blur on each.</summary>
    public static KitGesture Tab(string root = Stage, Func<IPage, Task>? arrange = null) => new("focus and blur by Tab", async page =>
    {
        await page.EvaluateAsync(
            "s => { const b = document.createElement('button'); b.id = '__before'; document.querySelector(s).before(b); b.focus(); }", root);
        var stops = 0;
        do
        {
            await StrikeAsync(page, "Tab");
            stops++;
        }
        while (stops < 12 && await page.EvaluateAsync<bool>("s => document.querySelector(s).contains(document.activeElement)", root));

        for (; stops > 0; stops--)
        {
            await StrikeAsync(page, "Shift+Tab");
        }
    }, arrange);

    /// <summary>What a popup is put through: opened by a press, walked by pointer and keys, closed by Escape and from outside.</summary>
    public static KitGesture[] Popup(string trigger, string popup) =>
    [
        Press("open by a press", trigger),
        Pointer(popup, Pressing(trigger), "pointer over the open popup"),
        Cursor(Pressing(trigger)),
        Escape(Pressing(trigger)),
        Outside(Pressing(trigger)),
    ];

    /// <summary>Text typed into <paramref name="selector" />, the focus left in it.</summary>
    public static KitGesture Typing(string selector, string text, string name = "typing", Func<IPage, Task>? arrange = null) =>
        new(name, page => TypeAsync(page, selector, text), arrange);

    /// <summary>Typing, and then Tab out of the field: what a value that is sent on change waits for.</summary>
    public static KitGesture TypingThenLeaving(string selector, string text) => new("typing, then leaving the field", async page =>
    {
        await TypeAsync(page, selector, text);
        await StrikeAsync(page, "Tab");
    });

    /// <summary>The pointer across <paramref name="selector" /> from side to side, a few pixels at a time.</summary>
    public static KitGesture Across(string selector, string name) => new(name, async page =>
    {
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        var y = box.Y + (box.Height / 2);
        await GlideAsync(page, (AwayX, AwayY), (box.X + 2, y), 1);
        await GlideAsync(page, (box.X + 2, y), (box.X + box.Width - 2, y), 25);
        await GlideAsync(page, (box.X + box.Width - 2, y), (AwayX, AwayY), 3);
    });

    /// <summary>A drag along <paramref name="selector" />, the button held at the end or let go.</summary>
    public static KitGesture Drag(string selector, bool release, Func<IPage, Task>? arrange = null) =>
        new(release ? "drag and release" : "drag, still held", page => DragAsync(page, selector, release), arrange);

    /// <summary>A drag that starts on <paramref name="selector" /> and goes <paramref name="by" /> pixels along.</summary>
    public static KitGesture DragBy(string selector, int by, bool release) => new(release ? "drag and release" : "drag, still held", async page =>
    {
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        var (x, y) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await HoldAndGlideAsync(page, (x, y), (x + by, y), release);
    });

    /// <summary>A press on one part and then on another (the same one when only one is named).</summary>
    public static KitGesture Twice(string name, string first, string? second = null, Func<IPage, Task>? arrange = null) => new(name, async page =>
    {
        await page.Locator(first).First.ClickAsync();
        await BeatAsync(page);
        await page.Locator(second ?? first).First.ClickAsync();
    }, arrange);

    /// <summary>A press with the other button, as a gesture and as an arrangement.</summary>
    public static KitGesture RightPress(string name, string selector) => new(name, RightPressing(selector));

    /// <inheritdoc cref="RightPress" />
    public static Func<IPage, Task> RightPressing(string selector) =>
        page => page.Locator(selector).First.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });

    /// <summary>Focus put on <paramref name="selector" />, as an arrangement.</summary>
    public static Func<IPage, Task> Focusing(string selector) => page => page.Locator(selector).First.FocusAsync();

    /// <summary>The pointer put over <paramref name="selector" />, as an arrangement.</summary>
    public static Func<IPage, Task> Hovering(string selector) => page => page.Locator(selector).First.HoverAsync();

    /// <summary>The keys that move a cursor, pressed where the focus is.</summary>
    public static KitGesture Keys(string name, Func<IPage, Task>? arrange, params string[] keys) => new(name, async page =>
    {
        foreach (var key in keys)
        {
            await StrikeAsync(page, key);
        }
    }, arrange);

    /// <summary>The keys every list, grid and menu answers to.</summary>
    public static KitGesture Cursor(Func<IPage, Task>? arrange, string name = "cursor keys") =>
        Keys(name, arrange, "ArrowDown", "ArrowDown", "ArrowUp", "ArrowRight", "ArrowLeft", "End", "Home", "PageDown", "PageUp");

    /// <summary>A press on <paramref name="selector" />.</summary>
    public static KitGesture Press(string name, string selector, Func<IPage, Task>? arrange = null) =>
        new(name, page => page.Locator(selector).First.ClickAsync(), arrange);

    /// <summary>Escape.</summary>
    public static KitGesture Escape(Func<IPage, Task> arrange, string name = "close by Escape") =>
        new(name, page => page.Keyboard.PressAsync("Escape"), arrange);

    /// <summary>A press on the page beside the component.</summary>
    public static KitGesture Outside(Func<IPage, Task> arrange, string name = "close by a press outside") =>
        new(name, page => page.Mouse.ClickAsync(AwayX - 5, AwayY - 5), arrange);

    /// <summary>Text typed into <paramref name="selector" />, as an arrangement.</summary>
    public static Func<IPage, Task> TypingInto(string selector, string text) => async page =>
    {
        await page.Locator(selector).First.FocusAsync();
        await page.Keyboard.TypeAsync(text);
    };

    /// <summary>A list opened by <paramref name="trigger" /> and searched for <paramref name="text" />, as an arrangement.</summary>
    public static Func<IPage, Task> SearchingFor(string trigger, string field, string text) => async page =>
    {
        await page.Locator(trigger).First.ClickAsync();
        await TypingInto(field, text)(page);
    };

    /// <summary>A press on <paramref name="selector" />, as an arrangement.</summary>
    public static Func<IPage, Task> Pressing(string selector) => page => page.Locator(selector).First.ClickAsync();

    /// <summary>
    ///     One animation frame of the page's time, and the server's answer to whatever the page sent in it: what
    ///     follows each step of a gesture, so the next step is made on a page that has heard the one before.
    /// </summary>
    public static async Task BeatAsync(IPage page)
    {
        await page.Clock.RunForAsync(FrameMs);
        await KitSocket.AnsweredAsync(page);
    }

    /// <summary>A drag from one end of <paramref name="selector" /> towards the other, without letting go.</summary>
    public static async Task DragAsync(IPage page, string selector, bool release)
    {
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        var y = box.Y + (box.Height / 2);
        await HoldAndGlideAsync(page, (box.X + (box.Width * 0.2f), y), (box.X + (box.Width * 0.8f), y), release);
    }

    // Text typed a key at a time into the field, the focus left in it.
    private static async Task TypeAsync(IPage page, string selector, string text)
    {
        await page.Locator(selector).First.FocusAsync();
        foreach (var character in text)
        {
            await page.Keyboard.TypeAsync(character.ToString());
            await BeatAsync(page);
        }
    }

    private static async Task StrikeAsync(IPage page, string key)
    {
        await page.Keyboard.PressAsync(key);
        await BeatAsync(page);
    }

    // The button goes down at one place and the pointer is taken to another in twelve steps, a beat after each.
    private static async Task HoldAndGlideAsync(IPage page, (float X, float Y) from, (float X, float Y) to, bool release)
    {
        await page.Mouse.MoveAsync(from.X, from.Y);
        await page.Mouse.DownAsync();
        await GlideAsync(page, from, to, DragSteps);
        if (release)
        {
            await page.Mouse.UpAsync();
        }
    }

    // The pointer from one place to another in even steps, a beat after each: a move a frame, as a hand makes them.
    private static async Task GlideAsync(IPage page, (float X, float Y) from, (float X, float Y) to, int steps)
    {
        for (var step = 1; step <= steps; step++)
        {
            await page.Mouse.MoveAsync(from.X + ((to.X - from.X) * step / steps), from.Y + ((to.Y - from.Y) * step / steps));
            await BeatAsync(page);
        }
    }

    private static async Task SweepAsync(IPage page, string root)
    {
        var parts = await page.EvaluateAsync<float[][]>(Parts, root);
        (float X, float Y) at = (AwayX, AwayY);
        foreach (var part in parts)
        {
            await GlideAsync(page, at, (part[0], part[1]), 3);
            at = (part[0], part[1]);
        }

        await GlideAsync(page, at, (AwayX, AwayY), 3);
    }
}
