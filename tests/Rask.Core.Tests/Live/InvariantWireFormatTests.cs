using System.Globalization;
using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// Everything Rask puts on the wire — reconciliation keys, DOM event payloads, field-path indices —
// is culture-neutral by contract, because the two ends are Rask and the browser rather than Rask and
// a person. These tests run those paths under a deliberately hostile ambient culture so that a change
// dropping an IFormatProvider fails here rather than in production under a locale nobody develops in.
//
// sv-SE and de-DE are chosen for what they break: sv-SE formats a negative number with U+2212 MINUS
// SIGN instead of '-', and de-DE reads '.' as a group separator, so "1.5" parses as 15.
//
// The culture lookups are asserted rather than skipped. A skip here would be indistinguishable from a
// pass, and these are exactly the assertions that must not quietly stop running; the unit gate runs on
// the server CLR, where ICU is present. A globalization-invariant runtime fails with the message below.
public partial class InvariantWireFormatTests : global::Rask.Core.RaskMarkup
{
    private static CultureInfo Hostile(string name)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(name);
            Assert.Equal(name, culture.Name);
            return culture;
        }
        catch (CultureNotFoundException e)
        {
            throw new InvalidOperationException(
                $"'{name}' is unavailable, so this runtime cannot prove invariance. It is running with "
                + "globalization turned off (InvariantGlobalization / PredefinedCulturesOnly / "
                + "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT). Run the unit gate on a runtime with ICU.", e);
        }
    }

    [Fact]
    [RestoreCulture]
    public void KeyString_NegativeIntKey_KeepsAsciiHyphen_UnderSwedish()
    {
        CultureInfo.CurrentCulture = Hostile("sv-SE");

        // sv-SE renders -1 as "−1" (U+2212). The key is baked into the HTML the client already holds,
        // so a culture-dependent spelling breaks keyed reconciliation the moment the culture changes.
        Assert.Equal("<li data-rask-key=\"-1\"></li>", Li.Key(-1).ToHtml());
    }

    [Fact]
    [RestoreCulture]
    public void KeyString_DecimalAndDateKeys_AreInvariant_UnderGerman()
    {
        CultureInfo.CurrentCulture = Hostile("de-DE");

        // de-DE would render 1.5 as "1,5" and the date as "02.01.2026".
        Assert.Equal("<li data-rask-key=\"1.5\"></li>", Li.Key(1.5m).ToHtml());

        // Note the spelling: IFormattable with a null format gives the INVARIANT short-date pattern
        // (MM/dd/yyyy), not ISO. A key only has to be stable and unique, not readable, so this is
        // deliberate — the general IFormattable arm keeps one rule for every formattable key type
        // rather than special-casing the date types. What matters is that it does not vary by locale.
        Assert.Equal("<li data-rask-key=\"01/02/2026\"></li>", Li.Key(new DateOnly(2026, 1, 2)).ToHtml());
    }

    [Fact]
    [RestoreCulture]
    public void EventPayload_StringEncodedNumbers_AreReadInvariantly_UnderGerman()
    {
        CultureInfo.CurrentCulture = Hostile("de-DE");

        // The client serialises DOM numbers with JS formatting, which is invariant. Read under de-DE
        // without a provider, "1.5" becomes 15 — a wheel/pointer delta off by a factor of ten.
        using var doc = JsonDocument.Parse("""{"deltaY":"1.5","clientX":"-3"}""");
        Assert.Equal(1.5, EventPayload.ReadDouble(doc.RootElement, "deltaY"));
        Assert.Equal(-3, EventPayload.ReadInt(doc.RootElement, "clientX"));
    }

    // Unlike the double above, this one is a CONTRACT guard rather than a caught bug, and it is worth
    // being precise about which: integer parsing turns out to be culture-robust for the shapes the
    // client actually sends. NumberStyles.Integer admits no group or decimal separator in any culture,
    // and .NET accepts the ASCII hyphen even where the culture's NegativeSign is U+2212 — so reverting
    // the fix does not turn this test red. It stays because the invariant parse is the contract for
    // wire data, and because the sibling double path shows what it costs when that contract lapses.
    [Fact]
    [RestoreCulture]
    public void ScrollEvent_NegativeOffset_IsReadInvariantly_UnderSwedish()
    {
        CultureInfo.CurrentCulture = Hostile("sv-SE");

        using var doc = JsonDocument.Parse(
            """{"scrollTop":"-120","clientHeight":"600","scrollHeight":"2400"}""");
        var scroll = ScrollEvent.FromJson(doc.RootElement);

        Assert.Equal(-120, scroll.ScrollTop);
        Assert.Equal(600, scroll.ClientHeight);
        Assert.Equal(2400, scroll.ScrollHeight);
    }

    [Fact]
    [RestoreCulture]
    public void ScopeId_IsStable_UnderHostileCulture()
    {
        // A scope id is baked into scoped-CSS class names at build time (BakeScopedAssetsTask invokes
        // this very method by reflection) and recomputed at runtime. The two must agree byte-for-byte
        // on any machine, under any locale, or scoped CSS silently stops matching.
        var before = global::Rask.Core.ScopedCss.CssScoper.ScopeIdFor(typeof(InvariantWireFormatTests));

        CultureInfo.CurrentCulture = Hostile("sv-SE");
        var after = global::Rask.Core.ScopedCss.CssScoper.ScopeIdFor(typeof(InvariantWireFormatTests));

        Assert.Equal(before, after);
    }
}
