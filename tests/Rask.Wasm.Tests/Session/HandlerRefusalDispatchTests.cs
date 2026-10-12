using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Wasm.Tests.Infrastructure;
using Rask.Wire;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of a handler outside a form that refuses: the dispatch that carried the click
// answers with the refusal's own sentence in the toast, on the page as the handler left it.
[Collection("WasmSession")]
public class HandlerRefusalDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_delete_that_is_refused_answers_with_its_sentence_in_the_toast_and_the_page_it_was_on()
    {
        var (session, _) = NewSession<RefusedDeleteApp>(
            configure: static services => services.AddSingleton<IToaster, Toaster>(), diffMode: DiffMode);
        var click = MarkupAssert.RequireAttr(Html(await session.InitialRenderAsync()), "data-rask-on-click");

        var result = await session.DispatchAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = click, type = "click" })));

        var html = Html(result);
        Assert.Contains($"<li class=\"Error\" data-rask-key=\"0\">{RefusedDeleteApp.Sentence}</li>", html, StringComparison.Ordinal);
        Assert.Contains("attempts=1", html, StringComparison.Ordinal);
        Assert.Contains("<li>ABC-123</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_click_after_a_refused_one_is_answered()
    {
        var (session, _) = NewSession<RefusedDeleteApp>(
            configure: static services => services.AddSingleton<IToaster, Toaster>(), diffMode: DiffMode);
        var click = MarkupAssert.RequireAttr(Html(await session.InitialRenderAsync()), "data-rask-on-click");
        var frame = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = click, type = "click" }));
        await session.DispatchAsync(frame);

        var result = await session.DispatchAsync(frame);

        Assert.Contains("attempts=2", Html(result), StringComparison.Ordinal);
    }

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

internal sealed partial class RefusedDeleteApp : Component
{
    internal const string Sentence = "The trailer cannot be deleted: a waybill refers to it.";

    private int _attempts;

    protected override Component? HeadAssets => Title["refused-delete"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"attempts={_attempts}"],
        Ul[Li["ABC-123"]],
        Button.OnClick(Delete)["Delete"],
        ToastOutlet.Template(static (toasts, _) => Ul[toasts.Select(static t => (Component)Li.Key(t.Id).Class(t.Level.ToString())[t.Message])]),
    ];

    private async Task Delete()
    {
        _attempts++;
        await Task.Yield();
        throw new Refused();
    }

    private sealed class Refused() : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = [new FieldFailure(Sentence, [])];
    }
}
