using System.Buffers;
using System.Text;
using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Locks in the diff wire format. Each op is a positional JSON array whose shape is
// fixed per kind — the client interpreter (rask.js / rask.wasm.js applyDiff) reads
// op[0] for the kind, op[1] for the path, and trailing slots by position per kind.
// Changes to this contract must update both interpreters in lockstep.
public class LivePayloadDiffTests
{
    [Fact]
    public void BuildPayloadUtf8Diff_SerializesKindAndOps()
    {
        var ops = new List<EditOp>
        {
            new(EditOpKind.UpdateText, new[] { 0, 1, 0 }, null, "new value"),
            new(EditOpKind.SetAttribute, new[] { 1, 2 }, "class", "row-active"),
            new(EditOpKind.RemoveAttribute, new[] { 0, 3 }, "disabled", null),
            new(EditOpKind.RemoveSubtree, new[] { 0, 4 }, null, null, 5)
        };

        var output = new ArrayBufferWriter<byte>(256);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("diff", root.GetProperty("kind").GetString());

        var opsArr = root.GetProperty("ops").EnumerateArray().ToList();
        Assert.Equal(4, opsArr.Count);

        // UpdateText: [k, path, value]
        Assert.Equal((int)EditOpKind.UpdateText, opsArr[0][0].GetInt32());
        Assert.Equal(new[] { 0, 1, 0 }, opsArr[0][1].EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal("new value", opsArr[0][2].GetString());

        // SetAttribute: [k, path, name, value]
        Assert.Equal((int)EditOpKind.SetAttribute, opsArr[1][0].GetInt32());
        Assert.Equal("class", opsArr[1][2].GetString());
        Assert.Equal("row-active", opsArr[1][3].GetString());

        // RemoveAttribute: [k, path, name]
        Assert.Equal((int)EditOpKind.RemoveAttribute, opsArr[2][0].GetInt32());
        Assert.Equal("disabled", opsArr[2][2].GetString());
        Assert.Equal(3, opsArr[2].GetArrayLength());

        // RemoveSubtree: [k, path, domCount]
        Assert.Equal((int)EditOpKind.RemoveSubtree, opsArr[3][0].GetInt32());
        Assert.Equal(5, opsArr[3][2].GetInt32());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_WithJsInvokes_EmitsJsInvokesArray()
    {
        // Fire-and-forget IJSRuntime invokes ride the diff payload the same way they
        // ride the full-HTML payload, so a per-render js.InvokeVoidAsync no longer
        // forces the whole page onto the full-HTML path on the server runtime.
        var ops = new List<EditOp> { new(EditOpKind.UpdateText, new[] { 0, 1 }, null, "echo") };
        var invokes = new List<PendingJsInvoke>
        {
            new(7, "Rask.CodeSample.rendered", "[false]", 1, 0), new(8, "sessionStorage.getItem", "[\"k\"]", 0, 42)
        };

        var output = new ArrayBufferWriter<byte>(256);
        LivePayload.BuildPayloadUtf8Diff(output, ops, jsInvokes: invokes);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("diff", root.GetProperty("kind").GetString());

        var arr = root.GetProperty("jsInvokes").EnumerateArray().ToList();
        Assert.Equal(2, arr.Count);

        Assert.Equal(7, arr[0].GetProperty("id").GetInt64());
        Assert.Equal("Rask.CodeSample.rendered", arr[0].GetProperty("identifier").GetString());
        Assert.Equal("[false]", arr[0].GetProperty("argsJson").GetString());
        Assert.Equal(1, arr[0].GetProperty("resultType").GetInt32());
        // TargetInstanceId 0 is omitted (matches the full-HTML tail shape).
        Assert.False(arr[0].TryGetProperty("targetInstanceId", out _));

        Assert.Equal(8, arr[1].GetProperty("id").GetInt64());
        Assert.Equal(42, arr[1].GetProperty("targetInstanceId").GetInt64());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_WithoutJsInvokes_OmitsJsInvokesKey()
    {
        var ops = new List<EditOp> { new(EditOpKind.UpdateText, new[] { 0 }, null, "x") };

        var output = new ArrayBufferWriter<byte>(128);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("jsInvokes", out _));
    }

    [Fact]
    public void BuildPayloadUtf8Diff_WithHeadHtml_EmitsHeadFieldRoundTrips()
    {
        // The head fragment carries raw markup with '<', '>', and quotes — it must round-trip
        // exactly through the relaxed-escaping writer so the client can DOMParser it.
        var ops = new List<EditOp> { new(EditOpKind.UpdateText, new[] { 1, 0, 0 }, null, "x") };
        const string head = "<head><title>A &amp; B</title><link rel=\"stylesheet\" href=\"/x.css\"></head>";

        var output = new ArrayBufferWriter<byte>(256);
        LivePayload.BuildPayloadUtf8Diff(output, ops, headHtml: head);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(head, doc.RootElement.GetProperty("head").GetString());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_WithoutHeadHtml_OmitsHeadKey()
    {
        var ops = new List<EditOp> { new(EditOpKind.UpdateText, new[] { 0 }, null, "x") };

        var output = new ArrayBufferWriter<byte>(128);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("head", out _));
    }

    [Fact]
    public void BuildPayloadUtf8Diff_MoveSubtree_EncodesSourceSlot()
    {
        // MoveSubtree's source slot lives at op[2]. Source slot 0 is a legitimate
        // value (the moved node was at the first position), so the encoder must
        // emit it even when zero — otherwise the client would read undefined and
        // pick the wrong source.
        var ops = new List<EditOp>
        {
            new(EditOpKind.MoveSubtree, new[] { 1, 0 }, null, null, 7, true),
            new(EditOpKind.MoveSubtree, new[] { 1, 3 }, null, null, 0, true)
        };

        var output = new ArrayBufferWriter<byte>(128);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var opsArr = doc.RootElement.GetProperty("ops").EnumerateArray().ToList();

        Assert.Equal((int)EditOpKind.MoveSubtree, opsArr[0][0].GetInt32());
        Assert.Equal(7, opsArr[0][2].GetInt32());

        Assert.Equal((int)EditOpKind.MoveSubtree, opsArr[1][0].GetInt32());
        Assert.Equal(0, opsArr[1][2].GetInt32());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_PermutationBatch_EncodesMovesArray()
    {
        // PermutationBatch is [k, parentPath, moves] where moves is a flat
        // [dst0,src0,dst1,src1,…] array. op[1] is the PARENT path (no trailing slot);
        // the per-move dst/src live in op[2]. Zero is a legitimate slot, so the encoder
        // must emit every entry verbatim and in order.
        var ops = new List<EditOp>
        {
            new(EditOpKind.PermutationBatch, new[] { 1, 0, 0 }, null, null, trusted: true,
                moves: new[] { 95, 5, 0, 96 })
        };

        var output = new ArrayBufferWriter<byte>(128);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var opsArr = doc.RootElement.GetProperty("ops").EnumerateArray().ToList();

        Assert.Single(opsArr);
        Assert.Equal((int)EditOpKind.PermutationBatch, opsArr[0][0].GetInt32());
        Assert.Equal(new[] { 1, 0, 0 }, opsArr[0][1].EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Equal(new[] { 95, 5, 0, 96 }, opsArr[0][2].EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_InsertSubtree_EncodesHtmlAndDomCount()
    {
        var ops = new List<EditOp> { new(EditOpKind.InsertSubtree, new[] { 0, 2 }, null, "<li>new</li>", 1, true) };

        var output = new ArrayBufferWriter<byte>(128);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var op = doc.RootElement.GetProperty("ops")[0];

        // InsertSubtree: [k, path, html, domCount]
        Assert.Equal((int)EditOpKind.InsertSubtree, op[0].GetInt32());
        Assert.Equal("<li>new</li>", op[2].GetString());
        Assert.Equal(1, op[3].GetInt32());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_SetAttribute_NullValueEncodesAsNull()
    {
        // Bare HTML attributes (`disabled`, `required`) carry no value. The positional
        // format must still emit a slot so the client reads name and value from the
        // expected indices.
        var ops = new List<EditOp> { new(EditOpKind.SetAttribute, new[] { 0 }, "disabled", null) };

        var output = new ArrayBufferWriter<byte>(64);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var op = doc.RootElement.GetProperty("ops")[0];

        Assert.Equal((int)EditOpKind.SetAttribute, op[0].GetInt32());
        Assert.Equal("disabled", op[2].GetString());
        Assert.Equal(JsonValueKind.Null, op[3].ValueKind);
    }

    [Fact]
    public void BuildPayloadUtf8Diff_InternsAttributeNamesAppearingThreeOrMoreTimes()
    {
        // Three SetAttribute ops sharing "data-loaded" → emit one "names" entry, ops
        // reference it by integer index. Saves the duplicate name bytes on the wire.
        var ops = new List<EditOp>
        {
            new(EditOpKind.SetAttribute, new[] { 0, 0 }, "data-loaded", "true"),
            new(EditOpKind.SetAttribute, new[] { 0, 1 }, "data-loaded", "true"),
            new(EditOpKind.SetAttribute, new[] { 0, 2 }, "data-loaded", "true")
        };

        var output = new ArrayBufferWriter<byte>(256);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // names table is present with the interned attribute name.
        var names = root.GetProperty("names").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "data-loaded" }, names);

        // Each op's name slot is now a number (the index into names), not a string.
        foreach (var op in root.GetProperty("ops").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Number, op[2].ValueKind);
            Assert.Equal(0, op[2].GetInt32());
        }
    }

    [Fact]
    public void BuildPayloadUtf8Diff_NamesAppearingOnceOrTwice_StayInline()
    {
        // Below the 3-occurrence interning threshold: each name stays inline as a
        // string, no "names" envelope is emitted. Keeps small diffs from paying the
        // table-overhead tax that would net-cost bytes on payloads with no repetition.
        var ops = new List<EditOp>
        {
            new(EditOpKind.SetAttribute, new[] { 0, 0 }, "class", "a"),
            new(EditOpKind.SetAttribute, new[] { 0, 1 }, "class", "b"),
            new(EditOpKind.SetAttribute, new[] { 0, 2 }, "style", "color:red")
        };

        var output = new ArrayBufferWriter<byte>(256);
        LivePayload.BuildPayloadUtf8Diff(output, ops);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("names", out _));
        foreach (var op in root.GetProperty("ops").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, op[2].ValueKind);
        }
    }

    [Fact]
    public void BuildPayloadUtf8Diff_EmptyOps_ProducesEmptyOpsArray()
    {
        var output = new ArrayBufferWriter<byte>(64);
        LivePayload.BuildPayloadUtf8Diff(output, new List<EditOp>());

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.Empty(doc.RootElement.GetProperty("ops").EnumerateArray());
    }

    [Fact]
    public void BuildPayloadUtf8Diff_IncludesHistory_WhenProvided()
    {
        var output = new ArrayBufferWriter<byte>(128);
        LivePayload.BuildPayloadUtf8Diff(output, new List<EditOp>(), "/page/2", true);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/page/2", history.GetProperty("url").GetString());
        Assert.Equal("replace", history.GetProperty("action").GetString());
    }
}
