using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>A viewport: its size, what it measures of Flux under (<c>@390x844</c>), and whether it is a phone's.</summary>
public sealed record View(int Width, int Height, string Sized)
{
    /// <summary>Below Tailwind's <c>sm</c> (640px): touch, and the overlay scrollbars a phone has.</summary>
    public bool Phone => Width < 640;

    /// <summary>A behaviour command's <c>--view &lt;w&gt;x&lt;h&gt;</c>: the viewport it walks in, a desktop's unless given.</summary>
    public static View Of(Args args, int width = 1280, int height = 900)
    {
        var asked = args.Option("view");
        if (asked is null)
        {
            return new View(width, height, "");
        }

        var size = asked.Split('x').Select(int.Parse).ToArray();
        return new View(size[0], size[1], $"@{size[0]}x{size[1]}");
    }

    /// <summary>The options of a context with this screen.</summary>
    public BrowserNewContextOptions Context() => Screen.Of(Width, Height);
}

/// <summary>A browser context's screen.</summary>
public static class Screen
{
    /// <summary>
    ///     Below Tailwind's <c>sm</c> (640px) a PHONE's — touch, and the overlay scrollbars a phone has. A desktop browser
    ///     390px wide is no phone: there Flux's <c>scrollbar-gutter: stable</c> reserves 15px of a scrollbar nobody draws
    ///     and sets its date dialog 7.5px off centre, which no reader ever sees (measured on its date picker: 25.5px from
    ///     the left as a desktop, 33px as a phone, where the kit's is too).
    /// </summary>
    public static BrowserNewContextOptions Of(int width, int height) => width < 640
        ? new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height }, IsMobile = true, HasTouch = true }
        : new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height } };

    /// <summary>
    ///     A press as the screen makes it: a TAP on a phone (the touch, then the mouse events a browser makes of it), a
    ///     click of the mouse elsewhere. A phone has no pointer to hover with, so a walk drops its hover steps there.
    /// </summary>
    public static async Task Press(IPage page, double x, double y, bool phone)
    {
        if (phone)
        {
            await page.Touchscreen.TapAsync((float)x, (float)y);
        }
        else
        {
            await page.Mouse.ClickAsync((float)x, (float)y);
        }
    }

    /// <summary>Two of the page's frames, or a quarter of a second (<see cref="PageScripts.Frames" />).</summary>
    public static Task Frames(IPage page) => page.Run(PageScripts.Frames);

    /// <summary>
    ///     A finger's tap on <paramref name="target" /> (at <paramref name="at" />, or its centre), waited on until the
    ///     browser has made it a CLICK — the last of the mouse events it makes of a tap, so the hover they leave is in —
    ///     and then until that hover holds (<see cref="PageScripts.HoverSettled" />). Waited on by the event, not by a
    ///     time: on a loaded Linux runner those mouse events came after hoverSettled had given up, and Flux's phone
    ///     chevron was measured unhovered beside the kit's hovered one (#1260).
    /// </summary>
    public static async Task Tapped(IPage page, IElementHandle target, double[]? at = null)
    {
        await page.Run("""
            () => {
              window.__fluxParityTapped = false;
              document.addEventListener('click', () => { window.__fluxParityTapped = true; }, { capture: true, once: true });
            }
            """);
        if (at is not null)
        {
            await page.Touchscreen.TapAsync((float)at[0], (float)at[1]);
        }
        else
        {
            await target.TapAsync();
        }

        // A tap on what takes no pointer makes no click: ten seconds, then on as before.
        try
        {
            await page.WaitForFunctionAsync("() => window.__fluxParityTapped", null, new PageWaitForFunctionOptions { Timeout = 10000 });
        }
        catch (TimeoutException)
        {
            // as the script had it: on as before
        }

        await target.Run(PageScripts.HoverSettled);
    }
}
