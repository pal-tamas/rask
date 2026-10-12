using System.Collections.Concurrent;
using System.Text.Json;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Sixty charts, each telling the page its measured box in the same browser task, on a live SERVER page.
/// </summary>
/// <remarks>
///     The unit suites hold the two halves apart — the client's batch frame (<c>EventBatchFixture.ts</c>) and the
///     session's one render for it (<c>Rask.Server.Tests</c>' <c>EventBatchTests</c>). This is the seam: a real
///     <c>ResizeObserver</c> callback, the real socket, and the page drawn again once. Sent a frame each, the
///     same page was sixty round trips and sixty patches of a page that needed one.
/// </remarks>
public sealed class EventBatchTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string AllPlotted =
        "() => { const all = [...document.querySelectorAll('svg')]; return all.length === 60 && all.every(s => s.getAttribute('viewBox') === '0 0 150 50'); }";

    [Fact]
    public async Task Sixty_charts_measured_in_one_task_are_one_frame_out_and_one_render_back()
    {
        var sent = new ConcurrentQueue<string>();
        var received = new ConcurrentQueue<string>();
        await using var session = await HookSession.OpenAsync<SixtyChartsPage>(playwright, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) =>
            {
                socket.FrameSent += (_, frame) => sent.Enqueue(frame.Text ?? string.Empty);
                socket.FrameReceived += (_, frame) => received.Enqueue(frame.Text ?? string.Empty);
            };
            return page.AddInitScriptAsync(
                "new MutationObserver(() => { if (!window.plottedAt && (" + AllPlotted + ")()) window.plottedAt = performance.now(); })"
                + ".observe(document, {subtree: true, childList: true, attributes: true});");
        });
        var page = session.Page;

        await page.WaitForFunctionAsync(AllPlotted);
        await page.WaitForFunctionAsync("() => window.plottedAt > 0");
        var plottedAt = await page.EvaluateAsync<double>("() => window.plottedAt");
        // The ack follows the render, so once it is here every frame the batch caused has been counted.
        await WaitForAsync(() => received.Any(IsAck));

        var events = sent.Where(frame => !IsOfType(frame, "hello")).ToArray();
        var renders = received.Where(frame => !IsAck(frame)).ToArray();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"60 charts: plotted {plottedAt:F0} ms after navigation; {events.Length} frame(s) out, {renders.Length} render(s) back, {renders.Sum(r => r.Length)} chars");
        Assert.Equal(60, BatchedEvents(Assert.Single(events)));
        Assert.Single(renders);
        Assert.Empty(session.PageErrors);
    }

    private static async Task WaitForAsync(Func<bool> met)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!met() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(met());
    }

    private static bool IsAck(string frame) => IsOfType(frame, "ack");

    private static bool IsOfType(string frame, string type)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.TryGetProperty("type", out var t) && t.ValueEquals(type);
    }

    private static int BatchedEvents(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("events").GetArrayLength();
    }
}

/// <summary>Sixty charts of fifty points, six to a row, each 150 by 50 once the browser has laid them out.</summary>
public sealed partial class SixtyChartsPage : Component
{
    private sealed record Reading(int At, double Value);

    private static readonly Reading[][] Series =
        [.. Enumerable.Range(0, 60).Select(chart => Enumerable.Range(0, 50).Select(at => new Reading(at, Math.Sin((at + chart) / 5d) * 40)).ToArray())];

    // The handful of the kit's utilities the chart's own box needs; the page has no stylesheet.
    private const string Layout =
        "body{margin:0}.grid{display:grid;grid-template-columns:repeat(6,150px)}.relative{position:relative}"
        + ".absolute{position:absolute}.block{display:block}.inset-0{inset:0}.size-full{width:100%;height:100%}"
        + ".hidden{display:none}.wide{aspect-ratio:3/1}.pointer-events-none{pointer-events:none}";

    protected override Component? HeadAssets => [Markup.Title["sixty charts"], Markup.Style[Raw.Value(Layout)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div.Class("grid")[Charts()];

    private static Component Charts()
    {
        var charts = new List<Component>();
        for (var i = 0; i < Series.Length; i++)
        {
            charts.Add(Ui.Chart.Value(Series[i]).Class("wide").Key(i)[Ui.ChartSvg[Ui.ChartLine.Field((Reading r) => r.Value)]]);
        }

        return [.. charts.ToArray()];
    }
}
