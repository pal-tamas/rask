using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

// The props revival in the client runtime, driven against the production rask-external.js in node. Each of
// these guards a failure the C# side cannot see: the props JSON is right either way, and it is the browser
// that loses the call or hands the component a string where it expected a Date.
public sealed class ExternalReviveTests
{
    [Fact]
    public void A_handler_with_positions_sends_only_those_arguments()
    {
        if (Run() is not { } doc)
        {
            return;
        }

        Assert.True(doc.GetProperty("failure").ValueKind == JsonValueKind.Null, doc.GetProperty("failure").ToString());

        var pick = Dispatched(doc, "pick");
        Assert.Equal("external", pick.GetProperty("type").GetString());
        Assert.Equal(7, Assert.Single(pick.GetProperty("args").EnumerateArray()).GetInt32());
    }

    [Fact]
    public void An_empty_position_list_sends_no_arguments_at_all()
    {
        if (Run() is not { } doc)
        {
            return;
        }

        Assert.Equal(0, Dispatched(doc, "close").GetProperty("args").GetArrayLength());
    }

    [Fact]
    public void A_handler_without_positions_still_sends_every_argument()
    {
        // A hand-written island's callback carries no $a, and nothing about it changes.
        if (Run() is not { } doc)
        {
            return;
        }

        var args = Dispatched(doc, "legacy").GetProperty("args");
        Assert.Equal(2, args.GetArrayLength());
        Assert.Equal("two", args[1].GetString());
    }

    [Fact]
    public void A_handler_nested_inside_a_value_is_revived_too()
    {
        if (Run() is not { } doc)
        {
            return;
        }

        Assert.Equal("only", Assert.Single(Dispatched(doc, "deep").GetProperty("args").EnumerateArray()).GetString());
    }

    [Fact]
    public void A_date_tag_revives_into_a_Date()
    {
        if (Run() is not { } doc)
        {
            return;
        }

        Assert.True(doc.GetProperty("whenIsDate").GetBoolean(), "the $d tag was not revived into a Date");
        Assert.Equal("2026-09-11T08:00:00.000Z", doc.GetProperty("whenIso").GetString());
    }

    [Fact]
    public void An_object_that_merely_has_a_dollar_d_key_is_left_alone()
    {
        if (Run() is not { } doc)
        {
            return;
        }

        Assert.True(doc.GetProperty("lookKept").GetBoolean(), "an object with other keys beside $d was rewritten");
    }

    private static JsonElement? Run() => NodeFixture.Run("ExternalReviveFixture");

    private static JsonElement Dispatched(JsonElement doc, string id) =>
        doc.GetProperty("dispatched").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
}
