using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Authentication;

// The browser host's side of a page whose [Authorize] refuses the reader: a signed-in reader stays on its address
// and sees the forbidden sentence inside the layouts that let them in; someone not signed in is sent to sign in.
[Collection("WasmSession")]
public sealed class ForbiddenPageTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_forbidden_page_shows_the_sentence_inside_its_layout_and_keeps_its_address()
    {
        var session = Open(Person("ana"), "/wf/keys");

        var frame = await session.InitialRenderAsync();

        var html = Html(frame);
        Assert.Matches("<div id=\"wf-layout\">.*You do not have permission to view this page\\..*</div>", html);
        Assert.DoesNotContain("keys-content", html, StringComparison.Ordinal);
        Assert.False(HasHistory(frame));
    }

    [Fact]
    public async Task Someone_not_signed_in_is_still_sent_to_sign_in()
    {
        var session = Open(new ClaimsPrincipal(new ClaimsIdentity()), "/wf/keys");

        var frame = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(frame.AsMemory());
        Assert.StartsWith("/login?returnUrl=", doc.RootElement.GetProperty("history").GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("keys-content", Html(frame), StringComparison.Ordinal);
    }

    private WasmLiveSession Open(ClaimsPrincipal person, string path)
    {
        var (session, services) = NewSession<WasmForbidApp>(
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

    private static bool HasHistory(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.TryGetProperty("history", out _);
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

internal sealed partial class WasmForbidApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/wf")]
[Authorize]
internal sealed partial class WasmForbidLayout : Component
{
    protected override Component? Render() => Div.Id("wf-layout")[Span["vault"], Outlet];
}

[Route("keys")]
[ParentRoute(typeof(WasmForbidLayout))]
[Authorize(Roles = "admin")]
internal sealed partial class WasmForbidKeys : Component
{
    protected override Component? Render() => P["keys-content"];
}
