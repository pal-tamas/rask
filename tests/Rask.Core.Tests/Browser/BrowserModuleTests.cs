using System.Text.Json;
using Rask.TestSupport;

namespace Rask.Core.Tests.Browser;

/// <summary>
///     The shared browser layer, driven as ordinary TypeScript modules in a node subprocess.
/// </summary>
/// <remarks>
///     <para>
///         These modules were carved out of <c>rask-api.ts</c>, where every one of them was reachable
///         only through a dotted <c>IJSRuntime</c> identifier resolved against <c>window</c>. A build
///         and a type-check say the extraction compiles; they say nothing about whether the cookie
///         assignment string is still assembled the same way, so the mapping is asserted here.
///     </para>
///     <para>
///         Running under node is the point rather than a convenience. The fixture's imports evaluate
///         with no <c>window</c>, <c>document</c> or <c>navigator</c> in scope — so a module that
///         touched a DOM global at import time would fail the import, which is exactly what a Next or
///         Nuxt SERVER render would do to it. That is the one structural rule of this directory:
///         side effects belong in <c>globals.ts</c>, which the fixture deliberately never imports.
///     </para>
/// </remarks>
public class BrowserModuleTests
{
    private static JsonElement? Result => NodeFixture.Run("BrowserModuleFixture");

    [Fact]
    public void The_modules_import_with_no_DOM_present()
    {
        // Node is not required to build or test Rask; the browser-observable half is covered by E2E.
        if (Result is not { } r) return;

        Assert.True(r.GetProperty("importedWithoutADom").GetBoolean());
    }

    [Fact]
    public void A_position_is_flattened_out_of_the_live_GeolocationPosition()
    {
        if (Result is not { } r) return;

        Assert.Equal(51.5, r.GetProperty("fixLatitude").GetDouble());
        Assert.True(r.GetProperty("fixAltitudeIsNull").GetBoolean());

        // The timestamp rides on the position, not on coords — easy to drop in a rewrite, and nothing
        // downstream would notice until a caller tried to age a fix.
        Assert.Equal(1234, r.GetProperty("fixTimestampMs").GetInt64());
    }

    [Fact]
    public void A_watch_hands_back_a_stop_function_that_is_idempotent()
    {
        if (Result is not { } r) return;

        Assert.Equal(1, r.GetProperty("watchedCount").GetInt32());

        // Stopped twice in the fixture. Clearing twice would clear a watch id the browser may have
        // already reissued to someone else.
        Assert.Equal(1, r.GetProperty("clears").GetInt32());
    }

    [Fact]
    public void Cookies_are_decoded_on_read_and_assembled_on_write()
    {
        if (Result is not { } r) return;

        Assert.Equal("he llo", r.GetProperty("cookieRead").GetString());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("cookieMissing").ValueKind);

        var all = r.GetProperty("cookieAll");
        Assert.Equal("1", all.GetProperty("a").GetString());
        Assert.Equal("he llo", all.GetProperty("token").GetString());

        // Option order is part of the string, and the trailing flags are bare rather than `=true`.
        Assert.Equal(
            "token=he%20llo; max-age=60; path=/; samesite=Lax; secure",
            r.GetProperty("cookieSetWrite").GetString());

        // A delete is an expiry, and it only lands when it names the same path the cookie was set on.
        Assert.Equal("token=; max-age=0; path=/app", r.GetProperty("cookieDeleteWrite").GetString());
    }

    [Fact]
    public void The_media_query_conveniences_ask_the_real_query_strings()
    {
        if (Result is not { } r) return;

        Assert.True(r.GetProperty("prefersDark").GetBoolean());
        Assert.False(r.GetProperty("prefersReducedMotion").GetBoolean());

        var asked = r.GetProperty("mediaQueries").EnumerateArray().Select(q => q.GetString()).ToList();
        Assert.Equal(["(prefers-color-scheme: dark)", "(prefers-reduced-motion: reduce)"], asked);
    }

    [Fact]
    public void Network_info_finds_the_vendor_prefixed_object_and_defaults_the_numbers()
    {
        if (Result is not { } r) return;

        Assert.True(r.GetProperty("networkSupported").GetBoolean());

        // The fixture presents ONLY navigator.mozConnection — the shape Firefox has — so a fallback
        // chain that stopped at navigator.connection would report "unsupported" on a browser that
        // supports it.
        var network = r.GetProperty("network");
        Assert.Equal("3g", network.GetProperty("effectiveType").GetString());
        Assert.True(network.GetProperty("saveData").GetBoolean());

        // Absent numbers become 0 rather than undefined, so the C# record binds and a front end can
        // do arithmetic without a null check.
        Assert.Equal(0, network.GetProperty("downlink").GetDouble());
        Assert.Equal(0, network.GetProperty("rtt").GetDouble());
    }

    [Fact]
    public void A_storage_estimate_with_neither_figure_reports_zeroes()
    {
        if (Result is not { } r) return;

        var estimate = r.GetProperty("estimate");
        Assert.Equal(0, estimate.GetProperty("quota").GetInt64());
        Assert.Equal(0, estimate.GetProperty("usage").GetInt64());
    }
}
