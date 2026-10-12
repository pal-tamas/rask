using System.Text;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of an edit form whose field received text its type cannot hold: the stored value is
// not saved in its place. The field says what it could not read and the submit handler does not run.
[Collection("WasmSession")]
public class UnreadValueDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Theory]
    [InlineData("99999999999", "The number is out of range.")]
    [InlineData("6500.5", "Enter a whole number.")]
    public async Task A_number_that_does_not_fit_refuses_the_submit_that_follows_it(string typed, string sentence)
    {
        var (session, _) = NewSession<StoredWeightApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());
        var change = MarkupAssert.RequireAttr(initial, "data-rask-on-change");
        var submit = MarkupAssert.RequireAttr(initial, "data-rask-on-submit");

        var said = Html(await session.DispatchAsync(Frame(new { id = change, type = "change", value = typed })));
        var afterSubmit = await session.DispatchAsync(Frame(new { id = submit, type = "submit", form = new { } }));

        Assert.Contains($"<p class=\"said\">{sentence}</p>", said, StringComparison.Ordinal);
        Assert.Contains("saved=none;stored=6500;", said, StringComparison.Ordinal);
        // A refused submit leaves the page as the value's own frame drew it: the reply carries nothing to draw.
        Assert.Empty(afterSubmit);
    }

    private static byte[] Frame(object frame) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame));

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

internal sealed partial class StoredWeightApp : Component
{
    private readonly Tank _tank = new();
    private string _saved = "none";

    protected override Component? HeadAssets => Title["stored weight"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"saved={_saved};stored={_tank.Weight};"],
        Form.Model(_tank).OnSubmit(tank => _saved = tank.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture))[
            Input.Bind(() => _tank.Weight),
            Validation.Message.Template(messages => P.Class("said")[messages[0]]).For(() => _tank.Weight)
        ]
    ];

    private sealed class Tank
    {
        public int Weight { get; set; } = 6500;
    }
}
