using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteValueParserTests
{
    [Fact]
    public void A_string_parses_to_itself()
    {
        Assert.True(RouteValueParser.TryParse(typeof(string), "hello", out var v));
        Assert.Equal("hello", v);
    }

    [Fact]
    public void An_int_parses_with_the_invariant_culture()
    {
        Assert.True(RouteValueParser.TryParse(typeof(int), "42", out var v));
        Assert.Equal(42, v);
    }

    [Fact]
    public void A_double_parses_with_the_invariant_culture_under_the_German_culture()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.True(RouteValueParser.TryParse(typeof(double), "1.5", out var v));
            Assert.Equal(1.5, (double)v!);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void A_nullable_double_is_unwrapped_and_parsed()
    {
        Assert.True(RouteValueParser.TryParse(typeof(double?), "2.5", out var v));
        Assert.Equal(2.5, (double)v!);
    }

    [Fact]
    public void A_Guid_value_is_parsed_via_IParsable()
    {
        var raw = "11112222-3333-4444-5555-666677778888";

        Assert.True(RouteValueParser.TryParse(typeof(Guid), raw, out var v));
        Assert.Equal(Guid.Parse(raw), v);
    }

    [Fact]
    public void A_type_that_is_not_IParsable_does_not_parse()
    {
        Assert.False(RouteValueParser.TryParse(typeof(NotParsable), "anything", out var v));
        Assert.Null(v);
    }

    [Fact]
    public void Bad_input_for_an_int_does_not_parse()
    {
        Assert.False(RouteValueParser.TryParse(typeof(int), "not-a-number", out var v));
        Assert.Null(v);
    }

    [Fact]
    public void Repeated_parses_reuse_the_cached_parser()
    {
        Assert.True(RouteValueParser.TryParse(typeof(int), "1", out _));
        Assert.True(RouteValueParser.TryParse(typeof(int), "2", out var second));
        Assert.Equal(2, second);
    }

    private sealed class NotParsable
    {
    }
}
