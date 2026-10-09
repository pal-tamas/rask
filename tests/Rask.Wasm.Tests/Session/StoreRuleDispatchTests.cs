using System.Text;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;
using Rask.Wire;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of a rule the store owns: the dispatch that carried a field's change answers with
// the store's message under it, and the dispatch that carried the submit stops before the save.
[Collection("WasmSession")]
public class StoreRuleDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_committed_field_the_store_refuses_answers_with_the_message_under_it()
    {
        var (session, _) = NewSession<StoreRuledApp>(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var change = MarkupAssert.RequireAttr(Html(initial), "data-rask-on-change");

        var result = await session.DispatchAsync(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id = change, type = "change", value = "Budapest" })));

        var html = Html(result);
        Assert.Contains("<p class=\"said\">That name is in use.</p>", html, StringComparison.Ordinal);
        Assert.Contains("asked=Name", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_submit_the_store_refuses_answers_with_the_message_and_never_runs_the_save()
    {
        var (session, _) = NewSession<StoreRuledApp>(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var submit = MarkupAssert.RequireAttr(Html(initial), "data-rask-on-submit");

        var result = await session.DispatchAsync(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { id = submit, type = "submit", form = new { } })));

        var html = Html(result);
        Assert.Contains("<p class=\"said\">That name is in use.</p>", html, StringComparison.Ordinal);
        Assert.Contains("saves=0", html, StringComparison.Ordinal);
        Assert.Contains("error=none", html, StringComparison.Ordinal);
    }

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

internal sealed partial class StoreRuledApp : Component
{
    private readonly Place _place = new();
    private int _saves;

    static StoreRuledApp() => RaskValidation.RegisterStoreRules(typeof(Place), _ => PlaceRules.Instance);

    protected override Component? HeadAssets => Title["store ruled"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"saves={_saves}"],
        P[$"asked={string.Join(',', _place.Asked)}"],
        Form.Model(_place).OnSubmit(_ => _saves++)[f =>
        [
            Input.Bind(() => _place.Name).Blur(),
            Validation.Message.Template(messages => P.Class("said")[messages[0]]).For(() => _place.Name),
            P[$"error={f.Error?.Message ?? "none"}"],
        ]]
    ];

    private sealed class Place
    {
        public string Name { get; set; } = "Budapest";

        internal List<string> Asked { get; } = [];
    }

    private sealed class PlaceRules : IStoreRules
    {
        internal static readonly PlaceRules Instance = new();

        public ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken)
        {
            ((Place)model).Asked.Add(field ?? "all");
            return new ValueTask<IReadOnlyList<FieldFailure>>([new FieldFailure("That name is in use.", [nameof(Place.Name)])]);
        }
    }
}
