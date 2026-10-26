using System.Text;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The browser host's half of a secret field: the page says how many times it heard it and never the text, whole
// page or diff, and what the reader typed into it is in no frame the session sends.
[Collection("WasmSession")]
public class SecretHeardDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    private const string Typed = "hunter2-correct-horse";

    [Fact]
    public async Task A_password_the_submit_empties_is_heard_as_a_count_in_the_page_and_its_text_is_not_in_it()
    {
        var (session, _) = NewSession<WasmSecretApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());

        var answer = await session.DispatchAsync(Batch(
            new { id = Attr(initial, "data-rask-on-change"), type = "change", value = Typed },
            new { id = Attr(initial, "data-rask-on-submit"), type = "submit", form = new { } }));

        Assert.Contains("data-rask-heard=\"1\"", Html(answer), StringComparison.Ordinal);
        Assert.DoesNotContain(":" + Typed, Encoding.UTF8.GetString(answer), StringComparison.Ordinal);
    }

    private static string Attr(string html, string name) => Rask.TestSupport.MarkupAssert.RequireAttr(html, name);

    private static byte[] Batch(params object[] events) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "batch", events }));

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

// The same, over the diff the session sends when it can: every frame of the batch, which renders once between the
// change and the submit.
[Collection("WasmSession")]
public sealed class SecretHeardDiffDispatchTests : ResettingTestBase, IDisposable
{
    private const string Typed = "hunter2-correct-horse";
    private readonly List<string> _frames = [];

    public SecretHeardDiffDispatchTests()
        : base(LiveDiffMode.Forced) =>
        JSInterop.AppliedFrames = _frames;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task A_password_the_submit_empties_is_heard_in_a_diff_as_a_count_and_never_as_text()
    {
        var (session, _) = NewSession<WasmSecretApp>(diffMode: DiffMode);
        var initial = Encoding.UTF8.GetString(await session.InitialRenderAsync());
        var html = JsonDocument.Parse(initial).RootElement.GetProperty("html").GetString()!;

        await session.DispatchAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = "batch",
            events = new object[]
            {
                new { id = Rask.TestSupport.MarkupAssert.RequireAttr(html, "data-rask-on-change"), type = "change", value = Typed },
                new { id = Rask.TestSupport.MarkupAssert.RequireAttr(html, "data-rask-on-submit"), type = "submit", form = new { } },
            },
        })));

        var all = string.Concat(_frames);
        Assert.Contains("data-rask-heard", all, StringComparison.Ordinal);
        Assert.DoesNotContain(":" + Typed, all, StringComparison.Ordinal);
    }
}

internal sealed partial class WasmSecretApp : Component
{
    private readonly Draft _draft = new();
    private string _sent = "";

    protected override Component? HeadAssets => Title["secret"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"sent={_sent};"],
        Form.Model(_draft).OnSubmit(Send)[Input.Bind(() => _draft.Password).Type(InputType.Password)]
    ];

    private void Send(Draft draft)
    {
        _sent = draft.Password.Length > 0 ? "done" : "empty";
        draft.Password = "";
    }

    private sealed class Draft
    {
        public string Password { get; set; } = "";
    }
}
