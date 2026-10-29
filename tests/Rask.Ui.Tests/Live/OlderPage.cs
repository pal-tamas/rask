using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.UiTests.Live;

/// <summary>
///     A live page whose browser is behind it: it loaded the first page, and every event it sends is read from that
///     one, while each event the page hears is answered with a render. The events after the first arrive one, two,
///     three pages late — a reader faster than the page answers.
/// </summary>
internal sealed class OlderPage : IDisposable
{
    private readonly SessionRenderCache _cache = new();
    private readonly Component _page;

    public OlderPage(Component page)
    {
        _page = page;
        Loaded = Render();
    }

    /// <summary>The page the browser loaded, and reads every event from.</summary>
    public string Loaded { get; }

    /// <summary>The ids of the loaded page's handlers for one DOM event, in document order.</summary>
    public List<string> Ids(string domEvent) =>
        [.. Regex.Matches(Loaded, $"data-rask-on-{domEvent}=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups[1].Value)];

    /// <summary>The loaded page's one handler for a DOM event.</summary>
    public string Id(string domEvent) => Assert.Single(Ids(domEvent));

    /// <summary>A field's value announced from the loaded page.</summary>
    public Task<bool> TypedAsync(string id, string value) =>
        HearAsync(id, JsonSerializer.Serialize(new { value, v = 0 }));

    /// <summary>A press on the loaded page.</summary>
    public Task<bool> PressedAsync(string id) => HearAsync(id, "{\"v\":0}");

    /// <summary>One event of the session, read from the loaded page: its handler, then the render that answers it.</summary>
    public async Task<bool> HearAsync(string id, string payload)
    {
        using var frame = JsonDocument.Parse(payload);
        var heard = await _page.TryInvokeHandlerAsync(id, frame.RootElement);
        Render();

        return heard;
    }

    public void Dispose() => _cache.Dispose();

    // Renders, and says the browser was sent the result.
    private string Render()
    {
        string html;
        using (FrameSinkScope.Push(_cache.PrepareCurrentBuffer()))
        {
            html = _page.RenderAsLiveRoot();
        }

        _cache.TryComputeDiff([], out _, html);
        _page.HandlersSent();

        return html;
    }
}
