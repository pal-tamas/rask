using Rask.Api.Client;

namespace Rask.Api.Tests;

/// <summary>
///     Building a URL from caller-supplied values.
/// </summary>
/// <remarks>
///     The failure these guard against is not a 404 — it is a request that reaches a <b>different
///     endpoint than the one the caller named</b>, carrying whatever credentials
///     <c>ApiClientOptions.ConfigureRequestAsync</c> just attached. Escaping alone does not achieve
///     that: <c>.</c> and <c>..</c> are unreserved, so <see cref="Uri.EscapeDataString(string)" />
///     leaves them intact and <see cref="HttpClient" /> then resolves them away against its base
///     address.
/// </remarks>
public sealed class ApiUriTests
{
    [Theory]
    [InlineData("a/b")]
    [InlineData("a?b=c")]
    [InlineData("a#b")]
    [InlineData("a&b")]
    [InlineData("a b")]
    [InlineData("ä ö")]
    [InlineData("a\r\nb")]
    public void A_value_cannot_add_structure_to_the_path(string value)
    {
        var segment = ApiUri.Segment(value);

        Assert.DoesNotContain('/', segment);
        Assert.DoesNotContain('?', segment);
        Assert.DoesNotContain('#', segment);
        Assert.DoesNotContain('&', segment);
        Assert.DoesNotContain('\r', segment);
        Assert.DoesNotContain('\n', segment);
    }

    [Theory]
    [InlineData("..", "%2E%2E")]
    [InlineData(".", "%2E")]
    public void A_dot_segment_is_encoded_so_it_cannot_climb_the_path(string value, string expected)
    {
        // Uri.EscapeDataString leaves these alone. Unencoded, `orders.Get("..")` on
        // /api/tenants/{tenant}/orders puts `GET /api/orders` on the wire — a different endpoint,
        // reached with the caller's credentials.
        Assert.Equal(expected, ApiUri.Segment(value));
    }

    [Fact]
    public void An_encoded_dot_segment_still_survives_the_round_trip_as_a_value()
    {
        // Encoding rather than rejecting, because ".." is a legal name. A server decodes %2E%2E to the
        // two characters and does not re-normalise a decoded segment into a traversal.
        Assert.Equal("..", Uri.UnescapeDataString(ApiUri.Segment("..")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_route_value_is_refused_rather_than_collapsing_the_segment(string? value)
    {
        // An empty segment shortens the path, so /api/items/{id} becomes /api/items/ — which routing
        // matches to the COLLECTION endpoint, reached with a verb meant for one item. Two empty
        // segments in a row are worse still: //host is a network-path reference, and the request
        // leaves for another origin entirely.
        Assert.Throws<ArgumentException>(() => ApiUri.Segment(value));
    }

    [Fact]
    public void An_omitted_query_parameter_is_left_out_rather_than_sent_empty()
    {
        Assert.Equal("?page=2", ApiUri.Query(("page", 2), ("filter", null)));
        Assert.Equal(string.Empty, ApiUri.Query(("filter", null)));
    }

    [Fact]
    public void A_query_value_cannot_inject_another_parameter()
    {
        var query = ApiUri.Query(("q", "a&admin=true"));

        Assert.Equal("?q=a%26admin%3Dtrue", query);
    }

    [Fact]
    public void A_collection_repeats_the_name_the_way_the_binder_reads_it_back()
    {
        Assert.Equal("?tag=a&tag=b", ApiUri.Query(("tag", new[] { "a", "b" })));
    }
}
