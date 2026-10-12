using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Wasm.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of a save that throws: the dispatch that carried the submit answers with the toast.
// The browser host has no Development switch of its own, so the exception's words never leave the log.
[Collection("WasmSession")]
public class SaveFaultDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_save_that_throws_answers_with_the_toast_and_no_word_of_the_exception()
    {
        var (session, _) = NewSession<ThrowingSaveApp>(
            configure: static services => services.AddSingleton<IToaster, Toaster>(), diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var submit = MarkupAssert.RequireAttr(Html(initial), "data-rask-on-submit");

        var result = await session.DispatchAsync(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id = submit, type = "submit", form = new { } })));

        var html = Html(result);
        Assert.Contains("<li class=\"Error\" data-rask-key=\"0\">The save failed. Please try again.</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
        Assert.Contains("saves=1", html, StringComparison.Ordinal);
    }

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

internal sealed partial class ThrowingSaveApp : Component
{
    private readonly Place _place = new();
    private int _saves;

    protected override Component? HeadAssets => Title["throwing-save"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"saves={_saves}"],
        Form.Model(_place).OnSubmit(Save)[Input.Bind(() => _place.Name)],
        ToastOutlet.Template(static (toasts, _) => Ul[toasts.Select(static t => (Component)Li.Key(t.Id).Class(t.Level.ToString())[t.Message])]),
    ];

    private void Save(Place place)
    {
        _saves++;
        throw new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'.");
    }

    private sealed class Place
    {
        public string Name { get; set; } = "Budapest";
    }
}
