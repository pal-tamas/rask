using System.Text.Json;

namespace Rask.Core.Tests.Browser;

/// <summary>
///     The shared browser layer, driven as ordinary TypeScript modules in a node subprocess.
/// </summary>
/// <remarks>
///     <para>
///         These modules were carved out of <c>rask-api.ts</c>, where every one of them was reachable
///         only through a dotted <c>IJSRuntime</c> identifier resolved against <c>window</c>. A build
///         and a type-check say the extraction compiles; they say nothing about whether each request
///         is still assembled the same way, so the mapping is asserted here.
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
        Assert.Equal("TooManyAttempts", failure.GetProperty("error").GetString());
        Assert.Equal("Too many attempts.", failure.GetProperty("message").GetString());
    }

    [Fact]
    public void The_recovery_calls_post_to_their_own_routes_with_the_csrf_header()
    {
        if (Result is not { } r) return;

        Assert.Equal(
            "/api/auth/forgot-password", r.GetProperty("authForgotRequest").GetProperty("url").GetString());
        Assert.Equal(
            "/api/auth/reset-password", r.GetProperty("authResetRequest").GetProperty("url").GetString());
        Assert.Equal(
            "/api/auth/confirm-email", r.GetProperty("authConfirmRequest").GetProperty("url").GetString());

        // State-changing, so all three carry the header — the same defence as register and login.
        foreach (var name in new[] { "authForgotRequest", "authResetRequest", "authConfirmRequest" })
        {
            Assert.Equal(
                "1", r.GetProperty(name).GetProperty("headers").GetProperty("X-Rask-Auth").GetString());
        }
    }

    [Fact]
    public void A_body_less_success_is_read_as_a_success()
    {
        if (Result is not { } r) return;

        // 202 and 204 with nothing in them. Reading these the way login is read — parse the body into
        // a user — would turn every successful recovery call into a failure the caller cannot explain.
        Assert.True(r.GetProperty("authForgotOk").GetBoolean());
        Assert.True(r.GetProperty("authResetOk").GetBoolean());
        Assert.True(r.GetProperty("authConfirmOk").GetBoolean());
    }

    [Fact]
    public void A_stale_reset_link_reports_the_token_rather_than_the_password()
    {
        if (Result is not { } r) return;

        // "Ask for a new link" and "pick a longer password" are different instructions, and the server
        // has already told them apart. Flattening them here would undo that.
        Assert.Equal("InvalidToken", r.GetProperty("authResetFailure").GetProperty("error").GetString());
    }

    [Fact]
    public void A_passkey_ceremony_refuses_where_there_is_no_browser_rather_than_throwing()
    {
        if (Result is not { } r) return;

        // node has no navigator.credentials, which is what a server render looks like. Both ceremonies have to
        // report a refusal the caller can render, and neither should reach the network to find that out.
        Assert.False(r.GetProperty("authPasskeysSupported").GetBoolean());
        Assert.Equal("NotAllowed", r.GetProperty("authAddPasskeyFailure").GetProperty("error").GetString());
        Assert.Equal("NotAllowed", r.GetProperty("authPasskeySignInFailure").GetProperty("error").GetString());
        Assert.True(r.GetProperty("authPasskeyCeremonyMadeNoRequest").GetBoolean());
    }

    [Fact]
    public void Removing_a_passkey_posts_to_its_own_route_with_the_csrf_header()
    {
        if (Result is not { } r) return;

        var request = r.GetProperty("authRemovePasskeyRequest");

        Assert.True(r.GetProperty("authRemovePasskeyOk").GetBoolean());
        Assert.Equal("/api/auth/passkeys/remove", request.GetProperty("url").GetString());
        Assert.Equal("1", request.GetProperty("headers").GetProperty("X-Rask-Auth").GetString());
    }
}
