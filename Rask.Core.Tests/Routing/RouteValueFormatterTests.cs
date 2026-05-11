using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteValueFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsEmpty() => Assert.Equal(string.Empty, RouteValueFormatter.Format(null));

    [Fact]
    public void Format_String_PercentEncodesReservedCharacters() =>
        Assert.Equal("a%20b%2Fc", RouteValueFormatter.Format("a b/c"));

    [Fact]
    public void Format_BoolTrue_RendersLowercaseTrue() =>
        Assert.Equal("true", RouteValueFormatter.Format(true));

    [Fact]
    public void Format_BoolFalse_RendersLowercaseFalse() =>
        Assert.Equal("false", RouteValueFormatter.Format(false));

    [Fact]
    public void Format_Int_UsesInvariantCulture() =>
        Assert.Equal("42", RouteValueFormatter.Format(42));

    [Fact]
    public void Format_Decimal_UsesInvariantCulture_RegardlessOfCurrentCulture()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("1.5", RouteValueFormatter.Format(1.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Format_Guid_FormattedViaIFormattable()
    {
        var g = Guid.Parse("11112222-3333-4444-5555-666677778888");

        Assert.Equal("11112222-3333-4444-5555-666677778888", RouteValueFormatter.Format(g));
    }

    [Fact]
    public void Format_NonFormattableObject_FallsBackToToStringAndEncodes()
    {
        var obj = new NonFormattable();

        Assert.Equal("hello%2Fworld", RouteValueFormatter.Format(obj));
    }

    [Fact]
    public void Format_DateTime_UsesInvariantCulture()
    {
        var dt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var formatted = RouteValueFormatter.Format(dt);

        Assert.Contains("2026", formatted);
        Assert.DoesNotContain(",", formatted);
    }

    private sealed class NonFormattable
    {
        public override string ToString() => "hello/world";
    }
}
