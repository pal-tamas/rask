using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     The events one browser task produced cross into .NET as one <c>batch</c>: their handlers run in order and
///     the page is rendered once — unless a render in between is something a handler could tell.
/// </summary>
/// <remarks>
///     The frames a session pushes to the page are collected from the interop stub, so a test counts what the
///     browser would have applied rather than what the last call handed back.
/// </remarks>
[Collection("WasmSession")]
public sealed class EventBatchTests : ResettingTestBase, IDisposable
{
    private readonly List<string> _frames = [];

    public EventBatchTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _frames;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task Sixty_events_in_one_batch_run_in_order_and_are_answered_with_one_render()
    {
        var (session, page) = await StartAsync();
        var events = Enumerable.Range(0, EventBatchStubApp.Cells).Select(i => Event(page, $"hit{i}"));

        var answer = await session.DispatchAsync(Batch(events));

        var html = HtmlOf(Assert.Single(_frames));
        Assert.Equal(html, HtmlOf(Encoding.UTF8.GetString(answer)));
        Assert.Equal(Number(page, "walks") + 1, Number(html, "walks"));
        var stamps = Enumerable.Range(0, EventBatchStubApp.Cells).Select(i => Number(html, $"cell{i}")).ToArray();
        Assert.Equal(stamps.Order(), stamps);
        Assert.Equal(EventBatchStubApp.Cells, stamps.Distinct().Count());
    }

    [Fact]
    public async Task The_same_events_sent_a_call_each_are_each_rendered_as_they_always_were()
    {
        var (session, page) = await StartAsync();

        for (var i = 0; i < 5; i++)
        {
            await session.DispatchAsync(Utf8(Event(page, $"hit{i}")));
        }

        Assert.Equal(5, _frames.Count);
        Assert.Equal(Number(page, "walks") + 5, Number(HtmlOf(_frames[^1]), "walks"));
    }

    [Fact]
    public async Task Handlers_of_one_component_in_a_batch_each_see_the_render_of_the_one_before()
    {
        var (session, page) = await StartAsync();

        // `snap` closes over the count its render computed. Run before the render that follows `bump`, it would
        // log the count the page showed when the browser sent it; a call each, it has always logged the new one.
        await session.DispatchAsync(Batch([Event(page, "bump"), Event(page, "bump"), Event(page, "snap")]));

        Assert.Contains("log=bb[2];", HtmlOf(_frames[^1]));
        Assert.Equal(3, _frames.Count);
    }

    [Fact]
    public async Task A_handler_that_awaits_in_a_batch_still_renders_while_it_waits()
    {
        EventBatchStubApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (session, page) = await StartAsync();

        var batch = session.DispatchAsync(Batch([Event(page, "hit0"), Event(page, "slow"), Event(page, "hit1")]));
        await WaitForAsync(() => _frames.Count > 0);
        var waiting = HtmlOf(_frames[0]);
        EventBatchStubApp.Gate.TrySetResult();
        await batch;

        // The render made while the handler waits shows what it did so far, and the event before it.
        Assert.Contains("busy=True;", waiting);
        Assert.NotEqual("-", Text(waiting, "cell0"));
        Assert.Equal("-", Text(waiting, "cell1"));
        var done = HtmlOf(_frames[^1]);
        Assert.Contains("busy=False;log=s;", done);
        Assert.NotEqual("-", Text(done, "cell1"));
    }

    [Fact]
    public async Task A_handler_that_navigates_in_a_batch_sends_its_address_at_once_and_the_rest_still_run()
    {
        var (session, page) = await StartAsync();

        await session.DispatchAsync(Batch([Event(page, "hit0"), Event(page, "go"), Event(page, "hit1")]));

        Assert.Equal(2, _frames.Count);
        using var navigation = JsonDocument.Parse(_frames[0]);
        Assert.Equal("/elsewhere", navigation.RootElement.GetProperty("history").GetProperty("url").GetString());
        Assert.NotEqual("-", Text(HtmlOf(_frames[0]), "cell0"));
        Assert.Equal("-", Text(HtmlOf(_frames[0]), "cell1"));
        using var rest = JsonDocument.Parse(_frames[1]);
        Assert.False(rest.RootElement.TryGetProperty("history", out _));
        Assert.NotEqual("-", Text(HtmlOf(_frames[1]), "cell1"));
    }

    [Fact]
    public async Task A_handler_that_throws_in_a_batch_leaves_the_page_where_a_call_each_leaves_it()
    {
        var (batched, batchedPage) = await StartAsync();
        await batched.DispatchAsync(Batch([Event(batchedPage, "hit0"), Event(batchedPage, "boom"), Event(batchedPage, "bump")]));
        var together = _frames.ToArray();
        _frames.Clear();
        var (single, singlePage) = await StartAsync();

        foreach (var name in new[] { "hit0", "boom", "bump" })
        {
            await single.DispatchAsync(Utf8(Event(singlePage, name)));
        }

        // The root boundary's page replaces the app, so the event after the throw finds no handler: either way.
        Assert.Contains("Application error", HtmlOf(_frames[^1]));
        Assert.Contains("Application error", HtmlOf(together[^1]));
        Assert.Equal(2, _frames.Count);
        Assert.Single(together);
    }

    [Fact]
    public async Task A_batch_whose_last_event_names_no_handler_still_renders_what_the_others_did()
    {
        var (session, page) = await StartAsync();

        await session.DispatchAsync(Batch([Event(page, "hit0"), Event(page, "hit1"), """{"id":"no-such-handler"}"""]));

        var html = HtmlOf(Assert.Single(_frames));
        Assert.NotEqual("-", Text(html, "cell0"));
        Assert.NotEqual("-", Text(html, "cell1"));
    }

    [Fact]
    public async Task A_batch_skips_what_is_not_an_event_and_runs_the_rest()
    {
        var (session, page) = await StartAsync();

        await session.DispatchAsync(Batch(["7", "\"x\"", "null", """{"type":"navigate","path":"/nowhere"}""", Event(page, "hit0")]));

        Assert.NotEqual("-", Text(HtmlOf(Assert.Single(_frames)), "cell0"));
    }

    [Fact]
    public async Task A_batch_longer_than_the_client_ever_sends_runs_only_as_many_as_it_may()
    {
        var (session, page) = await StartAsync();

        await session.DispatchAsync(Batch(Enumerable.Repeat(Event(page, "bump"), EventBatch.MaxEvents + 50)));

        Assert.Equal(EventBatch.MaxEvents, Number(HtmlOf(_frames[^1]), "count"));
    }

    // The session, rendered once, and the page that render put up — with the frame of that render forgotten.
    private async Task<(WasmLiveSession Session, string Page)> StartAsync()
    {
        var (session, _) = NewSession<EventBatchStubApp>(diffMode: DiffMode);
        var page = HtmlOf(Encoding.UTF8.GetString(await session.InitialRenderAsync()));
        _frames.Clear();
        return (session, page);
    }

    private static async Task WaitForAsync(Func<bool> met)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!met() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(met());
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static byte[] Batch(IEnumerable<string> events) =>
        Utf8($$"""{"type":"batch","events":[{{string.Join(',', events)}}]}""");

    private static string Event(string page, string button)
    {
        var match = Regex.Match(page, $"id=\"{button}\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        Assert.True(match.Success, $"button '{button}' not found");
        return $$"""{"id":"{{match.Groups[1].Value}}","type":"click"}""";
    }

    private static string HtmlOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static string Text(string html, string field)
    {
        var match = Regex.Match(html, $"{field}=([^;]*);");
        Assert.True(match.Success, $"'{field}' not found");
        return match.Groups[1].Value;
    }

    private static int Number(string html, string field) =>
        int.Parse(Text(html, field), CultureInfo.InvariantCulture);
}
