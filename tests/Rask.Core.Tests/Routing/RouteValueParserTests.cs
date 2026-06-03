using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteValueParserTests
{
    [Fact]
    public void TryParse_String_RoundTrips()
    {
        Assert.True(RouteValueParser.TryParse(typeof(string), "hello", out var v));
        Assert.Equal("hello", v);
    }

    [Fact]
    public void TryParse_Int_ParsesInvariant()
    {
        Assert.True(RouteValueParser.TryParse(typeof(int), "42", out var v));
        Assert.Equal(42, v);
    }

    [Fact]
    public void TryParse_Int_RejectsCommaUnderGermanCulture()
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
    public void TryParse_NullableDouble_HandlesUnwrap()
    {
        Assert.True(RouteValueParser.TryParse(typeof(double?), "2.5", out var v));
        Assert.Equal(2.5, (double)v!);
    }

    [Fact]
    public void TryParse_Guid_ParsesViaIParsable()
    {
        var raw = "11112222-3333-4444-5555-666677778888";

        Assert.True(RouteValueParser.TryParse(typeof(Guid), raw, out var v));
        Assert.Equal(Guid.Parse(raw), v);
    }

    [Fact]
    public void TryParse_NonIParsableType_ReturnsFalse()
    {
        Assert.False(RouteValueParser.TryParse(typeof(NotParsable), "anything", out var v));
        Assert.Null(v);
    }

    [Fact]
    public void TryParse_Int_BadInput_ReturnsFalse()
    {
        Assert.False(RouteValueParser.TryParse(typeof(int), "not-a-number", out var v));
        Assert.Null(v);
    }

    [Fact]
    public void TryParse_RepeatedCalls_ReuseCachedParser()
    {
        Assert.True(RouteValueParser.TryParse(typeof(int), "1", out _));
        Assert.True(RouteValueParser.TryParse(typeof(int), "2", out var second));
        Assert.Equal(2, second);
    }

    private sealed class NotParsable
    {
    }
}
