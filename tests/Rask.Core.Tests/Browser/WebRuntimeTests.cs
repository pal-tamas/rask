using System.Text.Json;

namespace Rask.Core.Tests.Browser;

/// <summary>Rask.Web's runtime (<c>__raskWeb</c> in rask-api.ts), driven in a node subprocess.</summary>
public class WebRuntimeTests
{
    private static JsonElement? Result => NodeFixture.Run("WebRuntimeFixture");

    [Fact]
    public void A_list_the_browser_hands_over_crosses_its_items_beside_its_other_fields()
    {
        // Node is not required to build or test Rask; the browser-observable half is covered by E2E.
        if (Result is not { } r) return;

        var list = r.GetProperty("listData");
        var result = list.GetProperty("items")[0];

        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
        Assert.False(list.TryGetProperty("0", out _));
        Assert.True(result.GetProperty("isFinal").GetBoolean());
        Assert.Equal("hello", result.GetProperty("items")[0].GetProperty("transcript").GetString());
        Assert.Equal(0.9, result.GetProperty("items")[0].GetProperty("confidence").GetDouble());
    }

    [Fact]
    public void A_prefixed_global_is_supported_and_constructed_under_its_MDN_name()
    {
        if (Result is not { } r) return;

        Assert.True(r.GetProperty("prefixedIsSupported").GetBoolean());
        Assert.True(r.GetProperty("prefixedCreated").GetBoolean());
    }
}
