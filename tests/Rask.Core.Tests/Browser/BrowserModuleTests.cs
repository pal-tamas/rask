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
    public void Signing_in_posts_to_the_auth_prefix_with_the_csrf_header()
    {
        if (Result is not { } r) return;

        var request = r.GetProperty("authLoginRequest");

        // Relative, so the browser sends it same-origin and the HttpOnly cookie rides along. Nothing
        // in this module ever reads or writes a cookie — it cannot, and does not need to.
        Assert.Equal("/api/auth/login", request.GetProperty("url").GetString());
        Assert.Equal("POST", request.GetProperty("method").GetString());

        // The header no cross-site form, <img> or <script> can set. Without it the endpoint refuses.
        Assert.Equal("1", request.GetProperty("headers").GetProperty("X-Rask-Auth").GetString());
    }

    [Fact]
    public void A_server_render_calls_an_absolute_url_and_forwards_the_visitors_cookie()
    {
        if (Result is not { } r) return;

        var request = r.GetProperty("authMeRequest");

        // What a meta framework's SSR pass does: node has no page origin and no cookie jar, so the
        // base URL comes from RASK_BASE_URL and the cookie is forwarded by hand. The trailing slash
        // on the base is trimmed rather than doubled.
        Assert.Equal("http://127.0.0.1:8080/api/auth/me", request.GetProperty("url").GetString());
        Assert.Equal("GET", request.GetProperty("method").GetString());
        Assert.Equal("rask.auth=abc", request.GetProperty("headers").GetProperty("cookie").GetString());

        // GET /me changes nothing, so it carries no CSRF header — a server render that only reads the
        // current user should not have to know one exists.
        Assert.False(request.GetProperty("headers").TryGetProperty("X-Rask-Auth", out _));
    }

    [Fact]
    public void An_unreachable_server_reads_as_signed_out_rather_than_throwing()
    {
        if (Result is not { } r) return;

        // Anonymous closes doors rather than opening them, and a sign-out that cannot reach the server
        // is not a failure the caller can do anything with — the cookie is the server's to clear.
        Assert.True(r.GetProperty("authLogoutOnFailureResolves").GetBoolean());
        Assert.True(r.GetProperty("authMeOnFailureIsNull").GetBoolean());
    }

    [Fact]
    public void A_refusal_carries_the_servers_error_name_through_unchanged()
    {
        if (Result is not { } r) return;

        var failure = r.GetProperty("authFailureFromProblemDocument");

        // The NAME rather than a number, so a value added to AuthError later cannot silently become a
        // different one on the wire.
        Assert.Equal("LockedOut", failure.GetProperty("error").GetString());
        Assert.Equal("Too many attempts.", failure.GetProperty("message").GetString());
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
