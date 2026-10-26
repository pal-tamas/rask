using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Authentication;

// The browser host's side of [Guest]: a reader already signed in is sent to Then before the page is built, in place
// of its address; somebody not signed in sees the page.
[Collection("WasmSession")]
public sealed class GuestPageTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_first_load_of_a_guest_page_while_signed_in_shows_its_then_in_place_of_it()
    {
        var session = Open(Person("ana"), "/wg/login");

        var frame = await session.InitialRenderAsync();

        Assert.Equal(("/wg/home", "replace"), History(frame));
        Assert.Contains("home-content", Html(frame), StringComparison.Ordinal);
        Assert.DoesNotContain("login-content", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_to_a_guest_page_while_signed_in_lands_on_its_then_in_place_of_it()
    {
        var session = Open(Person("ana"), "/wg/start");
        await session.InitialRenderAsync();

        var frame = await session.DispatchAsync("""{"type":"navigate","path":"/wg/login","query":""}"""u8.ToArray());

        Assert.Equal(("/wg/home", "replace"), History(frame));
        Assert.DoesNotContain("login-content", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guest_page_is_shown_to_somebody_not_signed_in()
    {
        var session = Open(new ClaimsPrincipal(new ClaimsIdentity()), "/wg/login");

        var frame = await session.InitialRenderAsync();

        Assert.Contains("login-content", Html(frame), StringComparison.Ordinal);
        Assert.Equal((null, null), History(frame));
    }

    private WasmLiveSession Open(ClaimsPrincipal person, string path)
    {
        var (session, services) = NewSession<WasmGuestApp>(
            configure: s => s.AddLogging().AddAuthorizationCore().AddSingleton<IUserProvider>(new OnePerson(person)),
            diffMode: DiffMode);
        services.GetRequiredService<RouteState>().Path = path;
        return session;
    }

    private static ClaimsPrincipal Person(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test"));

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static (string? Url, string? Action) History(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.TryGetProperty("history", out var history)
            ? (history.GetProperty("url").GetString(), history.GetProperty("action").GetString())
            : (null, null);
    }

    private sealed class OnePerson(ClaimsPrincipal person) : IUserProvider
    {
        public ClaimsPrincipal Current => person;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}

internal sealed partial class WasmGuestApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/wg/start")]
internal sealed partial class WasmGuestStart : Component
{
    protected override Component? Render() => P["start-content"];
}

[Route("/wg/home")]
internal sealed partial class WasmGuestHome : Component
{
    protected override Component? Render() => P["home-content"];
}

[Route("/wg/login")]
[Guest(Then = "/wg/home")]
internal sealed partial class WasmGuestLogin : Component
{
    protected override Component? Render() => P["login-content"];
}
