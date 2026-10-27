using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A click sent into a connection at the moment it dies: the browser sends it again on the next connection, and it
///     runs once — whether the server never read it, or read it and its answer was what got lost (#1258).
/// </summary>
/// <remarks>
///     The death is staged between the page and the server: the frame that carries the click is held back, or let
///     through, and the page's end of the connection is cut at once, so no ack can come back on it.
/// </remarks>
public sealed class EventsAcrossReconnectTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 20_000 };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_click_sent_as_its_connection_dies_arrives_once(bool overHttp, bool reachedTheServer)
    {
        await using var host = await LiveServerHost.StartAsync<CounterPage>(blockWebSockets: overHttp);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var cut = 0;
        if (overHttp)
        {
            await page.RouteAsync(new Regex("/_rask/send/"), async route =>
            {
                if (route.Request.PostData?.Contains("\"seq\"", StringComparison.Ordinal) == true
                    && Interlocked.Exchange(ref cut, 1) == 0)
                {
                    if (reachedTheServer)
                    {
                        await route.FetchAsync();
                    }

                    await route.AbortAsync();
                    return;
                }

                await route.ContinueAsync();
            });
        }
        else
        {
            await page.RouteWebSocketAsync(new Regex("/rask/ws"), socket =>
            {
                var server = socket.ConnectToServer();
                socket.OnMessage(frame =>
                {
                    var text = frame.Text!;
                    var dies = text.Contains("\"seq\"", StringComparison.Ordinal) && Interlocked.Exchange(ref cut, 1) == 0;
                    if (!dies || reachedTheServer)
                    {
                        server.Send(text);
                    }

                    if (dies)
                    {
                        _ = socket.CloseAsync();
                    }
                });
                server.OnMessage(frame =>
                {
                    try
                    {
                        socket.Send(frame.Text!);
                    }
                    catch (PlaywrightException)
                    {
                        // The page's end is cut: what the server answered on this connection is lost, as in a real drop.
                    }
                });
            });
        }

        await page.GotoAsync(host.BaseUrl + "/");
        await page.WaitForFunctionAsync("() => document.body.inert === false");
        await page.ClickAsync("#bump");
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=1", Settled);
        await page.WaitForFunctionAsync("() => document.body.inert === false");
        await page.ClickAsync("#bump");

        // Had the click run twice, the next one would read three.
        await Expect(page.Locator("#count")).ToHaveTextAsync("count=2", Settled);
        Assert.Equal(1, Volatile.Read(ref cut));
    }
}
