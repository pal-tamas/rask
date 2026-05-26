using System.Buffers;
using System.Text;
using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Locks in the diff wire format. The JSON shape here is the contract the client
// interpreter (rask.js applyDiff) reads from. Changes need to update both sides.
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

        Assert.Equal((int)EditOpKind.UpdateText, opsArr[0].GetProperty("k").GetInt32());
        var path0 = opsArr[0].GetProperty("p").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(new[] { 0, 1, 0 }, path0);
        Assert.Equal("new value", opsArr[0].GetProperty("v").GetString());

        Assert.Equal((int)EditOpKind.SetAttribute, opsArr[1].GetProperty("k").GetInt32());
        Assert.Equal("class", opsArr[1].GetProperty("n").GetString());
        Assert.Equal("row-active", opsArr[1].GetProperty("v").GetString());

        Assert.Equal((int)EditOpKind.RemoveAttribute, opsArr[2].GetProperty("k").GetInt32());
        Assert.Equal("disabled", opsArr[2].GetProperty("n").GetString());
        Assert.False(opsArr[2].TryGetProperty("v", out _));

        Assert.Equal((int)EditOpKind.RemoveSubtree, opsArr[3].GetProperty("k").GetInt32());
        Assert.Equal(5, opsArr[3].GetProperty("l").GetInt32());
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
        LivePayload.BuildPayloadUtf8Diff(output, new List<EditOp>(), "/page/2", replace: true);

        var json = Encoding.UTF8.GetString(output.WrittenSpan);
        using var doc = JsonDocument.Parse(json);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("/page/2", history.GetProperty("url").GetString());
        Assert.Equal("replace", history.GetProperty("action").GetString());
    }
}
