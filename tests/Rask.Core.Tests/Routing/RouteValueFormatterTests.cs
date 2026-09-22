using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteValueFormatterTests
{
    [Fact]
    public void Null_formats_as_empty() => Assert.Equal(string.Empty, RouteValueFormatter.Format(null));

    [Fact]
    public void A_string_is_percent_encoded_for_reserved_characters() =>
        Assert.Equal("a%20b%2Fc", RouteValueFormatter.Format("a b/c"));

    [Fact]
    public void True_formats_as_lowercase_true() =>
        Assert.Equal("true", RouteValueFormatter.Format(true));

    [Fact]
    public void False_formats_as_lowercase_false() =>
        Assert.Equal("false", RouteValueFormatter.Format(false));

    [Fact]
    public void An_int_formats_with_the_invariant_culture() =>
        Assert.Equal("42", RouteValueFormatter.Format(42));

    [Fact]
    public void A_decimal_formats_with_the_invariant_culture_regardless_of_the_current_culture()
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
    public void A_Guid_value_is_formatted_via_IFormattable()
    {
        var g = Guid.Parse("11112222-3333-4444-5555-666677778888");

        Assert.Equal("11112222-3333-4444-5555-666677778888", RouteValueFormatter.Format(g));
    }

    [Fact]
    public void A_non_formattable_object_falls_back_to_ToString_and_is_encoded()
    {
        var obj = new NonFormattable();

        Assert.Equal("hello%2Fworld", RouteValueFormatter.Format(obj));
    }

    [Fact]
    public void A_DateTime_formats_with_the_invariant_culture()
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
