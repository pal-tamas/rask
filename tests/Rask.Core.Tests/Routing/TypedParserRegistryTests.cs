using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// Direct coverage of the reflection-free parser registry that makes route/form value binding work
// under full AOT (no MakeGenericMethod). TryGet is the path taken first by RouteValueParser, so a
// registered type never needs the dynamic fallback — this is the behaviour a full-AOT publish relies
// on, which cannot be observed via IsDynamicCodeSupported in a JIT test host.
public sealed class TypedParserRegistryTests
{
    public static TheoryData<Type, string, object> SeededPrimitives => new()
    {
        { typeof(int), "42", 42 },
        { typeof(long), "9000000000", 9_000_000_000L },
        { typeof(short), "7", (short)7 },
        { typeof(byte), "255", (byte)255 },
        { typeof(uint), "42", 42u },
        { typeof(double), "1.5", 1.5d },
        { typeof(decimal), "2.25", 2.25m },
        { typeof(float), "0.5", 0.5f },
        { typeof(bool), "true", true },
        { typeof(char), "x", 'x' },
        { typeof(Guid), "11112222-3333-4444-5555-666677778888", Guid.Parse("11112222-3333-4444-5555-666677778888") },
        { typeof(DateOnly), "2026-07-06", new DateOnly(2026, 7, 6) },
        { typeof(TimeOnly), "13:45", new TimeOnly(13, 45) },
        { typeof(TimeSpan), "01:02:03", new TimeSpan(1, 2, 3) },
    };

    [Theory]
    [MemberData(nameof(SeededPrimitives))]
    public void TryGet_SeededPrimitive_ParsesInvariant(Type type, string raw, object expected)
    {
        Assert.True(TypedParserRegistry.TryGet(type, out var parser));
        var (ok, value) = parser!(raw);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryGet_SeededParser_ReturnsFalse_OnBadInput()
    {
        Assert.True(TypedParserRegistry.TryGet(typeof(int), out var parser));
        var (ok, value) = parser!("not-a-number");
        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_UnregisteredCustomType_ReturnsFalse_UntilRegistered()
    {
        Assert.False(TypedParserRegistry.TryGet(typeof(Sku), out _));

        RaskBinding.RegisterParsable<Sku>();

        Assert.True(TypedParserRegistry.TryGet(typeof(Sku), out var parser));
        var (ok, value) = parser!("A-100");
        Assert.True(ok);
        Assert.Equal(new Sku("A-100"), value);
    }

    [Fact]
    public void RouteValueParser_UsesRegistry_ForRegisteredCustomType()
    {
        RaskBinding.RegisterParsable<Sku>();

        Assert.True(RouteValueParser.TryParse(typeof(Sku), "B-200", out var value));
        Assert.Equal(new Sku("B-200"), value);

        // Nullable wrapper unwraps to the same registered parser.
        Assert.True(RouteValueParser.TryParse(typeof(Sku?), "C-300", out var nullableValue));
        Assert.Equal(new Sku("C-300"), nullableValue);
    }

    private readonly record struct Sku(string Code) : IParsable<Sku>
    {
        public static Sku Parse(string s, IFormatProvider? provider) =>
            TryParse(s, provider, out var result) ? result : throw new FormatException($"Invalid SKU '{s}'.");

        public static bool TryParse(string? s, IFormatProvider? provider, out Sku result)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                result = new Sku(s);
                return true;
            }

            result = default;
            return false;
        }
    }
}
