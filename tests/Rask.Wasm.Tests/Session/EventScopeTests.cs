using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// In the browser as on a server: each event gets the app's scoped services fresh, and they go when it has rendered.
[Collection("WasmSession")]
public class EventScopeTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task Two_clicks_get_two_different_instances_of_a_scoped_service()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());

        await Click(session, html, "use");
        await Click(session, html, "use");

        var seen = services.GetRequiredService<WasmScopeLog>().Seen;
        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
    }

    [Fact]
    public async Task The_event_s_scope_is_disposed_once_the_event_has_rendered()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());

        await Click(session, html, "use");

        Assert.True(services.GetRequiredService<WasmScopeLog>().Seen[0].Disposed);
        Assert.Null(session.OpenedEventScope);
    }

    private static (WasmLiveSession Session, IServiceProvider Services) NewScopedSession() =>
        NewSession<WasmScopeApp>(configure: s =>
        {
            EventScopeCatalog.Add(s);
            s.AddSingleton<WasmScopeLog>();
            s.AddScoped<WasmScopeProbe>();
        });

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static async Task Click(WasmLiveSession session, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handler}}","type":"click"}"""));
    }
}

public sealed class WasmScopeLog
{
    public List<WasmScopeProbe> Seen { get; } = [];
}

public sealed class WasmScopeProbe(WasmScopeLog log) : IDisposable
{
    public bool Disposed { get; private set; }

    public void Use() => log.Seen.Add(this);

    public void Dispose() => Disposed = true;
}

public sealed partial class WasmScopeApp : Component
{
    protected override Component? Render() =>
        Div[
            Button.Id("use").OnClick(static () => Ambient.Services!.GetRequiredService<WasmScopeProbe>().Use())["use"]
        ];
}
