using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of #1245 and of a polling query in a hidden tab: the render that answers a submit says
// what it heard beside each field it emptied, and a visibility frame reaches the session's PageVisibility.
[Collection("WasmSession")]
public class HeardFieldDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task The_render_that_answers_a_submit_says_what_it_heard_beside_the_field_it_emptied()
    {
        var (session, _) = NewSession<WasmChatBoxApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());
        var change = MarkupAssert.RequireAttr(initial, "data-rask-on-change");
        var submit = MarkupAssert.RequireAttr(initial, "data-rask-on-submit");

        var answer = Html(await session.DispatchAsync(Batch(
            new { id = change, type = "change", value = "hello" }, new { id = submit, type = "submit", form = new { } })));

        Assert.Contains("sent=hello;", answer, StringComparison.Ordinal);
        Assert.Contains("data-rask-heard=\"1:hello\"", answer, StringComparison.Ordinal);
        Assert.Contains("value=\"\"", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_visibility_frame_holds_what_polls_until_the_tab_is_shown_again()
    {
        // As WasmHostBuilder registers it: one for the app, which is the tab.
        var (session, _) = NewSession<WasmChatBoxApp>(configure: services => services.AddSingleton<PageVisibility>(), diffMode: DiffMode);
        await session.InitialRenderAsync();
        var visibility = session.Services.GetRequiredService<PageVisibility>();

        var hiddenReply = await session.DispatchAsync(Frame(new { type = "visibility", hidden = true }));
        var whileHidden = visibility.Shown(CancellationToken.None).IsCompleted;
        await session.DispatchAsync(Frame(new { type = "visibility", hidden = false }));

        Assert.Empty(hiddenReply);
        Assert.False(whileHidden);
        Assert.True(visibility.Shown(CancellationToken.None).IsCompleted);
    }

    private static byte[] Frame(object frame) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame));

    private static byte[] Batch(params object[] events) => Frame(new { type = "batch", events });

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

internal sealed partial class WasmChatBoxApp : Component
{
    private readonly Draft _draft = new();
    private string _sent = "";

    protected override Component? HeadAssets => Title["chat"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"sent={_sent};"],
        Form.Model(_draft).OnSubmit(Send)[Input.Bind(() => _draft.Message)]
    ];

    private void Send(Draft draft)
    {
        _sent = draft.Message;
        draft.Message = "";
    }

    private sealed class Draft
    {
        public string Message { get; set; } = "";
    }
}
