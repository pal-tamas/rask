using System.Text;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;
using Rask.Wire;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of a refused save: the dispatch that carried the submit answers with the page,
// the message under its field, and the app still standing where a fault would have put the error page.
[Collection("WasmSession")]
public class FieldFailureDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_submit_the_handler_refuses_answers_with_the_message_under_its_field()
    {
        var (session, _) = NewSession<RefusingApp>(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var submit = MarkupAssert.RequireAttr(Html(initial), "data-rask-on-submit");

        var result = await session.DispatchAsync(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id = submit, type = "submit", form = new { } })));

        var html = Html(result);
        Assert.Contains("<p class=\"said\">That name is in use.</p>", html, StringComparison.Ordinal);
        Assert.Contains("saves=1", html, StringComparison.Ordinal);
        Assert.Contains("error=none", html, StringComparison.Ordinal);
    }

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

internal sealed partial class RefusingApp : Component
{
    private readonly Place _place = new();
    private int _saves;

    protected override Component? HeadAssets => Title["refusing"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"saves={_saves}"],
        Form.Model(_place).OnSubmit(Save)[f =>
        [
            Input.Bind(() => _place.Name),
            Validation.Message.Template(messages => P.Class("said")[messages[0]]).For(() => _place.Name),
            P[$"error={f.Error?.Message ?? "none"}"],
        ]]
    ];

    private void Save(Place place)
    {
        _saves++;
        throw new Refused(new FieldFailure("That name is in use.", [nameof(Place.Name)]));
    }

    private sealed class Place
    {
        public string Name { get; set; } = "Budapest";
    }

    private sealed class Refused(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }
}
