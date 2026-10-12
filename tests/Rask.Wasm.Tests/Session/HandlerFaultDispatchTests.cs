using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Wasm.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of a handler outside a form that throws: the dispatch that carried the click
// answers with the toast and its reference, on the page as the handler left it, and the error page is not
// drawn. The browser host has no Development switch of its own, so the exception's words stay in the log.
[Collection("WasmSession")]
public class HandlerFaultDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_delete_that_throws_answers_with_the_toast_and_its_reference_on_the_page_it_was_on()
    {
        var (session, _) = NewSession<FailingDeleteApp>(
            configure: static services => services.AddSingleton<IToaster, Toaster>(), diffMode: DiffMode);
        var click = MarkupAssert.RequireAttr(Html(await session.InitialRenderAsync()), "data-rask-on-click");

        var result = await session.DispatchAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = click, type = "click" })));

        var html = Html(result);
        Assert.Matches(
            "<li class=\"Error\" data-rask-key=\"0\">That did not work\\. Please try again\\. The problem has been recorded\\. "
            + "If it keeps happening, quote this reference: [A-Z0-9]{8}</li>",
            html);
        Assert.Contains("attempts=1", html, StringComparison.Ordinal);
        Assert.Contains("<li>ABC-123</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-error-boundary", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_click_after_a_failed_one_is_answered()
    {
        var (session, _) = NewSession<FailingDeleteApp>(
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

internal sealed partial class FailingDeleteApp : Component
{
    private int _attempts;

    protected override Component? HeadAssets => Title["failing-delete"];

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
        throw new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'.");
    }
}
