using System.Globalization;
using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-helper subclasses have no generated factory

namespace Rask.Core.Tests.Forms;

// Full primitive coverage for Input(Bind: () => model.X). The actual parse goes through
// RouteValueParser.TryParse, which dispatches to T.TryParse(raw, InvariantCulture, out)
// for anything implementing IParsable<T> — so every .NET primitive that ships that
// interface is supported. These tests pin:
//   1. DefaultInputType returns the right <input type=…> for every numeric kind.
//   2. End-to-end OnChange round-trips raw strings back into typed properties.
//   3. Invariant-culture formatting on the render side (so "3.14" doesn't become "3,14"
//      under a comma-decimal locale).
//   4. Empty input on a nullable property sets null.
//   4b. Empty input on a non-nullable value type sets default(T) so a cleared number/date
//       input doesn't snap back to its prior value on the next render.
//   5. Invalid input (non-empty, unparseable) leaves the prior value alone.
public partial class PrimitiveBindingTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    // Integer family — every signed/unsigned width plus native int.
    [InlineData(typeof(byte), "number")]
    [InlineData(typeof(sbyte), "number")]
    [InlineData(typeof(short), "number")]
    [InlineData(typeof(ushort), "number")]
    [InlineData(typeof(int), "number")]
    [InlineData(typeof(uint), "number")]
    [InlineData(typeof(long), "number")]
    [InlineData(typeof(ulong), "number")]
    [InlineData(typeof(nint), "number")]
    [InlineData(typeof(nuint), "number")]
    // Floating point.
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(Half), "number")]
    // Logic.
    [InlineData(typeof(bool), "checkbox")]
    // Temporal.
    [InlineData(typeof(DateTime), "datetime-local")]
    [InlineData(typeof(DateTimeOffset), "datetime-local")]
    [InlineData(typeof(DateOnly), "date")]
    [InlineData(typeof(TimeOnly), "time")]
    [InlineData(typeof(TimeSpan), "time")]
    // Text & identity & enum (the fallback path).
    [InlineData(typeof(string), "text")]
    [InlineData(typeof(char), "text")]
    [InlineData(typeof(Guid), "text")]
    public void DefaultInputType_MapsEveryPrimitive(Type clrType, string expected) =>
        Assert.Equal(expected, BindingHelpers.DefaultInputType(clrType));

    [Theory]
    [InlineData(typeof(int?), "number")]
    [InlineData(typeof(float?), "number")]
    [InlineData(typeof(double?), "number")]
    [InlineData(typeof(byte?), "number")]
    [InlineData(typeof(Half?), "number")]
    [InlineData(typeof(bool?), "checkbox")]
    [InlineData(typeof(DateOnly?), "date")]
    public void DefaultInputType_UnwrapsNullable(Type clrType, string expected) =>
        Assert.Equal(expected, BindingHelpers.DefaultInputType(clrType));

    [Fact]
    public async Task FloatProperty_OnChange_RoundTripsThroughInvariantCulture()
    {
        // Floating-point parsing is the headline case where culture matters — under a
        // comma-decimal locale ("3,14") and a period-decimal raw value ("3.14"), only the
        // invariant parser produces 3.14f. Asserting the round-trip pins both sides.
        var p = new NumericHolder { F = 1.5f };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.F)]);
        var html = page.Html;

        Assert.Contains("type=\"number\"", html);
        Assert.Contains("value=\"1.5\"", html);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"3.14\"}");

        Assert.True(ok);
        Assert.Equal(3.14f, p.F, 0.0001f);
    }

    [Fact]
    public async Task DoubleProperty_OnChange_ParsesScientificNotation()
    {
        var p = new NumericHolder { D = 0d };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.D)]);

        await page.ChangeAsync("{\"value\":\"6.022e23\"}");

        Assert.Equal(6.022e23, p.D, 1e20);
    }

    [Fact]
    public async Task DecimalProperty_OnChange_PreservesPrecision()
    {
        var p = new NumericHolder { M = 0m };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.M)]);

        await page.ChangeAsync("{\"value\":\"12345.6789\"}");

        Assert.Equal(12345.6789m, p.M);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("127", 127)]
    [InlineData("255", 255)]
    public async Task ByteProperty_OnChange_RoundTrips(string raw, byte expected)
    {
        var p = new NumericHolder { B = 1 };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.B)]);
        var html = page.Html;
        var changeId = Markup.Attr(html, "data-rask-on-change");

        await page.InvokeAsync(changeId!, $"{{\"value\":\"{raw}\"}}");

        Assert.Equal(expected, p.B);
    }

    [Fact]
    public async Task ULongProperty_OnChange_HandlesValuesAboveLongMaxValue()
    {
        // Specific to ulong: long.MaxValue + 1 must round-trip. If we accidentally routed
        // through long.TryParse this would fail.
        var p = new NumericHolder { Ul = 0ul };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Ul)]);
        await page.ChangeAsync("{\"value\":\"9223372036854775808\"}");

        Assert.Equal(9223372036854775808ul, p.Ul);
    }

    [Fact]
    public async Task HalfProperty_OnChange_RoundTrips()
    {
        var p = new NumericHolder { H = (Half)0 };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.H)]);
        await page.ChangeAsync("{\"value\":\"2.5\"}");

        Assert.Equal((Half)2.5, p.H);
    }

    [Fact]
    public async Task GuidProperty_OnChange_RoundTrips()
    {
        var p = new IdentityHolder { Token = Guid.Empty };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Token)]);
        var html = page.Html;
        var changeId = Markup.Attr(html, "data-rask-on-change");

        var fresh = Guid.NewGuid();
        await page.InvokeAsync(changeId!, $"{{\"value\":\"{fresh}\"}}");

        Assert.Equal(fresh, p.Token);
    }

    [Fact]
    public async Task CharProperty_OnChange_AcceptsSingleCharacter()
    {
        var p = new IdentityHolder { Letter = 'a' };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Letter)]);
        await page.ChangeAsync("{\"value\":\"Z\"}");

        Assert.Equal('Z', p.Letter);
    }

    [Fact]
    public async Task GuidProperty_InvalidInput_LeavesPriorValue()
    {
        var known = Guid.NewGuid();
        var p = new IdentityHolder { Token = known };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Token)]);

        await page.ChangeAsync("{\"value\":\"not-a-guid\"}");

        // Unparseable Guid text must NOT zero the field — TrySetTyped returns false, setter never runs.
        Assert.Equal(known, p.Token);
    }

    [Fact]
    public async Task CharProperty_MultiCharInput_LeavesPriorValue()
    {
        var p = new IdentityHolder { Letter = 'a' };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Letter)]);
        var changeId = page.HandlerId("change");

        // char.TryParse only accepts a single character — a two-char string fails to parse.
        await page.InvokeAsync(changeId!, "{\"value\":\"ab\"}");

        Assert.Equal('a', p.Letter);
    }

    [Fact]
    public async Task EnumProperty_OnChange_RoundTripsCaseInsensitively()
    {
        var p = new IdentityHolder { Level = Priority.Low };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Level)]);
        var changeId = page.HandlerId("change");

        // Enum binding goes through Enum.TryParse(ignoreCase: true), so a lower-cased member name binds.
        await page.InvokeAsync(changeId!, "{\"value\":\"high\"}");

        Assert.Equal(Priority.High, p.Level);
    }

    [Fact]
    public async Task EnumProperty_InvalidInput_LeavesPriorValue()
    {
        var p = new IdentityHolder { Level = Priority.High };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.Level)]);
        var changeId = page.HandlerId("change");

        // A string that is not a member name leaves the model untouched.
        await page.InvokeAsync(changeId!, "{\"value\":\"medium\"}");

        Assert.Equal(Priority.High, p.Level);
    }

    [Fact]
    public async Task NullableNumericProperty_EmptyInput_SetsNull()
    {
        var p = new NumericHolder { OptionalDouble = 9.9 };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.OptionalDouble)]);
        await page.ChangeAsync("{\"value\":\"\"}");

        Assert.Null(p.OptionalDouble);
    }

    [Fact]
    public async Task NumericProperty_InvalidInput_LeavesPriorValue()
    {
        var p = new NumericHolder { D = 1.5 };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.D)]);
        await page.ChangeAsync("{\"value\":\"not-a-number\"}");

        // Invalid input (non-empty, unparseable) must NOT silently zero the field — TrySetTyped
        // returns false and the setter is never called. The empty-input case is a separate
        // intentional clear and goes through the default(T) branch (see sibling test).
        Assert.Equal(1.5, p.D);
    }

    [Fact]
    public async Task NumericProperty_EmptyInput_SetsDefault()
    {
        var p = new NumericHolder { D = 1.5 };
        var page = RaskTest.Render(() => Form(p)[Input(() => p.D)]);
        await page.ChangeAsync("{\"value\":\"\"}");

        // Empty input on a non-nullable value type clears to default(T) so the user can
        // actually empty the field. Without this, the next render snaps the input back to
        // the prior value because TrySetTyped failed.
        Assert.Equal(0d, p.D);
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(nint))]
    [InlineData(typeof(nuint))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(Half))]
    [InlineData(typeof(char))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(TimeOnly))]
    [InlineData(typeof(TimeSpan))]
    public void EveryPrimitive_ImplementsIParsable(Type t)
    {
        // Pins the precondition the RouteValueParser depends on: every primitive type we
        // claim to bind to must implement IParsable<T>. If a future .NET version drops
        // that interface from one of these, this test catches it immediately.
        var iface = typeof(IParsable<>).MakeGenericType(t);
        Assert.True(iface.IsAssignableFrom(t),
            $"{t.Name} no longer implements IParsable<{t.Name}> — RouteValueParser would fall back to null parser.");
    }

    [Fact]
    public void FormatValue_FloatUsesInvariantCulture()
    {
        // Reproduce the comma-decimal locale problem deterministically with a culture switch.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma-decimal
            Assert.Equal("3.14", BindingHelpers.FormatValue(3.14f));
            Assert.Equal("3.14", BindingHelpers.FormatValue(3.14d));
            Assert.Equal("3.14", BindingHelpers.FormatValue(3.14m));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private sealed class NumericHolder
    {
        public byte B { get; set; }
        public sbyte Sb { get; set; }
        public short S { get; set; }
        public ushort Us { get; set; }
        public int I { get; set; }
        public uint Ui { get; set; }
        public long L { get; set; }
        public ulong Ul { get; set; }
        public nint Ni { get; set; }
        public nuint Nu { get; set; }
        public float F { get; set; }
        public double D { get; set; }
        public decimal M { get; set; }
        public Half H { get; set; }
        public double? OptionalDouble { get; set; }
    }

    private sealed class IdentityHolder
    {
        public Guid Token { get; set; }
        public char Letter { get; set; }
        public Priority Level { get; set; }
    }

    private enum Priority
    {
        Low,
        High
    }
}
