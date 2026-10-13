using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>One thing a reader does to a kit component: what is counted is every frame it puts on the socket.</summary>
/// <param name="Name">What the reader does, in a few words.</param>
/// <param name="Do">The gesture itself.</param>
/// <param name="Arrange">What comes before it and is not counted: opening the popup the gesture happens in.</param>
internal sealed record KitGesture(string Name, Func<IPage, Task> Do, Func<IPage, Task>? Arrange = null);

/// <summary>The gestures every component is put through, and the few steps they are made of.</summary>
internal static class KitGestures
{
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
            await page.Keyboard.PressAsync("Tab");
            stops++;
        }
        while (stops < 12 && await page.EvaluateAsync<bool>("s => document.querySelector(s).contains(document.activeElement)", root));

        for (; stops > 0; stops--)
        {
            await page.Keyboard.PressAsync("Shift+Tab");
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
    public static KitGesture Typing(string selector, string text, string name = "typing", Func<IPage, Task>? arrange = null) => new(name, async page =>
    {
        await page.Locator(selector).First.FocusAsync();
        await page.Keyboard.TypeAsync(text);
    }, arrange);

    /// <summary>Typing, and then Tab out of the field: what a value that is sent on change waits for.</summary>
    public static KitGesture TypingThenLeaving(string selector, string text) => new("typing, then leaving the field", async page =>
    {
        await page.Locator(selector).First.FocusAsync();
        await page.Keyboard.TypeAsync(text);
        await page.Keyboard.PressAsync("Tab");
    });

    /// <summary>The pointer across <paramref name="selector" /> from side to side, a few pixels at a time.</summary>
    public static KitGesture Across(string selector, string name) => new(name, async page =>
    {
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        var y = box.Y + (box.Height / 2);
        await page.Mouse.MoveAsync(box.X + 2, y);
        await page.Mouse.MoveAsync(box.X + box.Width - 2, y, new MouseMoveOptions { Steps = 25 });
        await page.Mouse.MoveAsync(AwayX, AwayY, new MouseMoveOptions { Steps = 3 });
    });

    /// <summary>A drag along <paramref name="selector" />, the button held at the end or let go.</summary>
    public static KitGesture Drag(string selector, bool release, Func<IPage, Task>? arrange = null) =>
        new(release ? "drag and release" : "drag, still held", page => DragAsync(page, selector, release), arrange);

    /// <summary>A drag that starts on <paramref name="selector" /> and goes <paramref name="by" /> pixels along.</summary>
    public static KitGesture DragBy(string selector, int by, bool release) => new(release ? "drag and release" : "drag, still held", async page =>
    {
        // The pointer is moved by coordinates, which reach nothing below the fold: a drag there pressed on no part at all.
        await page.Locator(selector).First.ScrollIntoViewIfNeededAsync();
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        var (x, y) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x + by, y, new MouseMoveOptions { Steps = 12 });
        if (release)
        {
            await page.Mouse.UpAsync();
        }
    });

    /// <summary>A press on one part and then on another (the same one when only one is named).</summary>
    public static KitGesture Twice(string name, string first, string? second = null, Func<IPage, Task>? arrange = null) => new(name, async page =>
    {
        await page.Locator(first).First.ClickAsync();
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
            await page.Keyboard.PressAsync(key);
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

    /// <summary>A press on <paramref name="selector" />, as an arrangement.</summary>
    public static Func<IPage, Task> Pressing(string selector) => page => page.Locator(selector).First.ClickAsync();

    /// <summary>The pointer onto the middle of <paramref name="selector" />, in steps.</summary>
    public static async Task OverAsync(IPage page, string selector)
    {
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), new MouseMoveOptions { Steps = 4 });
    }

    /// <summary>A drag from one end of <paramref name="selector" /> towards the other, without letting go.</summary>
    public static async Task DragAsync(IPage page, string selector, bool release)
    {
        var box = (await page.Locator(selector).First.BoundingBoxAsync())!;
        var y = box.Y + (box.Height / 2);
        await page.Mouse.MoveAsync(box.X + (box.Width * 0.2f), y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + (box.Width * 0.8f), y, new MouseMoveOptions { Steps = 12 });
        if (release)
        {
            await page.Mouse.UpAsync();
        }
    }

    private static async Task SweepAsync(IPage page, string root)
    {
        var parts = await page.EvaluateAsync<float[][]>(Parts, root);
        foreach (var part in parts)
        {
            await page.Mouse.MoveAsync(part[0], part[1], new MouseMoveOptions { Steps = 3 });
        }

        await page.Mouse.MoveAsync(AwayX, AwayY, new MouseMoveOptions { Steps = 3 });
    }
}
