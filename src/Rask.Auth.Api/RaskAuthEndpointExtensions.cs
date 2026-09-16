using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>Names shared by the auth endpoints and the clients that call them.</summary>
/// <remarks>
/// The contract itself lives in <see cref="AuthApi" />, in Rask.Wire, because the browser half cannot
/// reference this package — it carries Entity Framework, which must not reach a trimmed WebAssembly publish.
/// </remarks>
public static class RaskAuthDefaults
{
    /// <inheritdoc cref="AuthApi.RequestHeader" />
    public const string RequestHeader = AuthApi.RequestHeader;
}

/// <summary>Maps the register, sign-in, sign-out and current-user endpoints.</summary>
public static class RaskAuthEndpointExtensions
{
    /// <summary>
    /// Maps <c>register</c>, <c>login</c>, <c>logout</c> and <c>me</c> under
    /// <see cref="AuthOptions.ApiPrefix" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are what make the three flows reach the hosts that are not C#. A TypeScript front end, a
    /// meta framework's Node process and a WebAssembly client all speak to the same four routes, so
    /// "the same API in every host" is one contract rather than one per host.
    /// </para>
    /// <para>
    /// <b>Map this before the host's catch-all.</b> <c>UseRask</c>, <c>UseRaskSpa</c> and
    /// <c>UseRaskMeta</c> all end the pipeline with a fallback that answers every unmatched path — the
    /// meta host forwards it to Node — so auth endpoints mapped afterwards are never reached.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapRaskAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<AuthOptions>();

        var group = endpoints
            .MapGroup(options.ApiPrefix)
            // Antiforgery here would be ASP.NET's token pair, which a TypeScript client would have to
            // fetch and echo. The required header below is the same defence without the round-trip.
            .DisableAntiforgery();

        group.MapPost(AuthApi.Register, RegisterAsync);
        group.MapPost(AuthApi.Login, LoginAsync);
        // Cast to Delegate deliberately (ASP0016). LogoutAsync takes only an HttpContext, which is
        // exactly RequestDelegate's shape, so without the cast ASP.NET binds it as one and throws the
        // returned IResult away — the sign-out would happen and the response would not say so.
        group.MapPost(AuthApi.Logout, (Delegate)LogoutAsync);
        group.MapPost(AuthApi.LogoutOtherDevices, SignOutOtherDevicesAsync);
        group.MapPost(AuthApi.LogoutEverywhere, SignOutEverywhereAsync);
        group.MapGet(AuthApi.Me, Me);
        group.MapPost(AuthApi.ForgotPassword, ForgotPasswordAsync);
        group.MapPost(AuthApi.ResetPassword, ResetPasswordAsync);
        group.MapPost(AuthApi.ConfirmEmail, ConfirmEmailAsync);

        if (options.Passkeys)
        {
            group.MapPost(AuthApi.PasskeyRegisterOptions, PasskeyRegisterOptionsAsync);
            group.MapPost(AuthApi.PasskeyRegister, PasskeyRegisterAsync);
            group.MapPost(AuthApi.PasskeyLoginOptions, PasskeyLoginOptions);
            group.MapPost(AuthApi.PasskeyLogin, PasskeyLoginAsync);
            group.MapPost(AuthApi.PasskeyRemove, PasskeyRemoveAsync);
        }

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, RegisterRequest request, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var outcome = await accounts
            .RegisterAsync(request.Email, request.Password, request.FirstRunToken, Client(context), cancellationToken)
            .ConfigureAwait(false);

        return await CompleteAsync(context, outcome).ConfigureAwait(false);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context, LoginRequest request, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var outcome = await accounts
            .ValidateAsync(request.Email, request.Password, Client(context), cancellationToken)
            .ConfigureAwait(false);
        return await CompleteAsync(context, outcome, request.Remember).ConfigureAwait(false);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Ends every session of the caller except this one. <c>204</c>, or <c>401</c> when nobody is signed in.</summary>
    private static async Task<IResult> SignOutOtherDevicesAsync(
        HttpContext context, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        await accounts.SignOutOtherDevicesAsync(context.User, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Ends every session of the caller, this one included, and clears its cookie.</summary>
    private static async Task<IResult> SignOutEverywhereAsync(
        HttpContext context, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        await accounts.SignOutEverywhereAsync(context.User, cancellationToken).ConfigureAwait(false);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Emails a reset link. Answers the same way whether or not the address has an account.</summary>
    private static async Task<IResult> ForgotPasswordAsync(
        HttpContext context,
        ForgotPasswordRequest request,
        IAccounts accounts,
        CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var result = await accounts
            .SendPasswordResetAsync(request.Email, Client(context), cancellationToken)
            .ConfigureAwait(false);

        // 503 when the app has no mail battery, which is a misconfiguration of the server rather than anything
        // the caller did wrong — answering 401 would have a client show "check your email" over a message that
        // never left. 429 when the caller is asking too often.
        return result.Succeeded
            ? Results.Accepted()
            : Refuse(
                result,
                result.Error == AuthError.TooManyAttempts
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>Sets a new password from an emailed token.</summary>
    private static async Task<IResult> ResetPasswordAsync(
        HttpContext context, ResetPasswordRequest request, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var result = await accounts
            .ResetPasswordAsync(request.UserId, request.Token, request.Password, cancellationToken)
            .ConfigureAwait(false);

        // No session is issued here, deliberately. A reset link lives in an inbox and gets forwarded;
        // handing back a live cookie would make reading the email enough to be signed in, on top of the
        // password change. Signing in afterwards costs one form and proves the password was received.
        return result.Succeeded ? Results.NoContent() : Refuse(result, StatusCodes.Status400BadRequest);
    }

    /// <summary>Marks an address confirmed from an emailed token.</summary>
    private static async Task<IResult> ConfirmEmailAsync(
        HttpContext context, ConfirmEmailRequest request, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var result = await accounts
            .ConfirmEmailAsync(request.UserId, request.Token, cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? Results.NoContent() : Refuse(result, StatusCodes.Status400BadRequest);
    }

    /// <summary>The options for adding a passkey to the signed-in account.</summary>
    private static async Task<IResult> PasskeyRegisterOptionsAsync(
        HttpContext context, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        // Adding a passkey is adding a way into an account, so only that account's own live session may start it.
        if (AuthPrincipal.UserId(context.User) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var challenge = await accounts
            .BeginAddPasskeyAsync(userId, Origin(context), cancellationToken)
            .ConfigureAwait(false);

        return challenge is null
            ? Refuse(AuthResult.Fail(AuthError.NotAllowed), StatusCodes.Status400BadRequest)
            : Results.Ok(challenge);
    }

    /// <summary>Stores a verified passkey on the signed-in account.</summary>
    private static async Task<IResult> PasskeyRegisterAsync(
        HttpContext context,
        PasskeyRegistrationRequest request,
        IAccounts accounts,
        CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        if (AuthPrincipal.UserId(context.User) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await accounts
            .CompleteAddPasskeyAsync(userId, request, Origin(context), cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded ? Results.NoContent() : Refuse(result, StatusCodes.Status400BadRequest);
    }

    /// <summary>The options for signing in with a passkey. Anonymous, and says nothing about any account.</summary>
    private static IResult PasskeyLoginOptions(HttpContext context, IAccounts accounts)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        return accounts.BeginPasskeySignIn(Origin(context)) is { } challenge
            ? Results.Ok(challenge)
            : Refuse(AuthResult.Fail(AuthError.NotAllowed), StatusCodes.Status400BadRequest);
    }

    /// <summary>Signs in whoever signed the challenge.</summary>
    private static async Task<IResult> PasskeyLoginAsync(
        HttpContext context, PasskeyLoginRequest request, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var outcome = await accounts
            .CompletePasskeySignInAsync(request, Origin(context), Client(context), cancellationToken)
            .ConfigureAwait(false);

        // Exactly the password path from here: the same cookie, the same session row, the same bearer option.
        return await CompleteAsync(context, outcome, request.Remember).ConfigureAwait(false);
    }

    /// <summary>Removes one of the signed-in account's passkeys.</summary>
    private static async Task<IResult> PasskeyRemoveAsync(
        HttpContext context, RemovePasskeyRequest request, IAccounts accounts, CancellationToken cancellationToken)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        if (AuthPrincipal.UserId(context.User) is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(request.Id, out var passkeyId))
        {
            return Refuse(
                AuthResult.Fail(AuthError.PasskeyRejected, "That passkey is not on this account."),
                StatusCodes.Status400BadRequest);
        }

        var result = await accounts.RemovePasskeyAsync(userId, passkeyId, cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? Results.NoContent() : Refuse(result, StatusCodes.Status400BadRequest);
    }

    /// <summary>Who the caller is, or <c>204</c> when nobody.</summary>
    /// <remarks>
    /// This is the one endpoint every non-C# host needs: a TypeScript front end reads it on load, and a
    /// meta framework's server-side render calls it back over loopback carrying the visitor's own cookie,
    /// because Node cannot decrypt a Data-Protection-sealed cookie itself.
    /// </remarks>
    private static IResult Me(HttpContext context) => Describe(context.User);

    private static IResult Describe(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            // 204 rather than 401: "nobody is signed in" is a perfectly good answer to this question,
            // and a 401 would make every anonymous page load look like a failure in the client's logs.
            return Results.NoContent();
        }

        return Results.Ok(new CurrentUser(
            user.FindFirstValue(ClaimTypes.NameIdentifier),
            user.Identity.Name,
            user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()));
    }

    private static async Task<IResult> CompleteAsync(
        HttpContext context, AccountOutcome outcome, bool remember = false)
    {
        if (outcome is not { Result.Succeeded: true, Principal: { } principal })
        {
            // 401 for every refusal, carrying the code but never a hint about which account exists; 429 when the
            // caller is being throttled, so a client can say "wait a minute" rather than "wrong password".
            return Refuse(
                outcome.Result,
                outcome.Result.Error == AuthError.TooManyAttempts
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status401Unauthorized);
        }

        await context
            .SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = remember })
            .ConfigureAwait(false);

        // Described from the principal just signed in, not from context.User: SignInAsync writes the
        // cookie for the NEXT request and leaves this request's User as it was, so reading it here would
        // answer 204 to the caller that just succeeded.
        var described = Describe(principal);

        // The cookie is written either way. A caller asking for a token gets one BESIDE it rather than
        // instead of it: the two do not conflict, and a browser client that asked by accident is still
        // signed in the safe way. The token is only ever in the response body — never a cookie, never a
        // header — so nothing stores it on the caller's behalf.
        return WantsBearer(context) && BearerFor(context) is { } options
            ? Results.Ok(new BearerSession(
                BearerTokens.Issue(principal, options, TimeProvider.System),
                "Bearer",
                (int)options.BearerLifetime.TotalSeconds,
                Principal(principal)))
            : described;
    }

    /// <summary>Whether the caller asked for a bearer token rather than relying on the cookie.</summary>
    /// <remarks>
    ///     A header rather than a body field, so every endpoint that completes a sign-in answers the same
    ///     way without each request type growing a flag — and so a client sets it once, next to the
    ///     request header it already has to send.
    /// </remarks>
    private static bool WantsBearer(HttpContext context) =>
        string.Equals(
            context.Request.Headers[AuthApi.AuthModeHeader].ToString(),
            AuthApi.BearerMode,
            StringComparison.OrdinalIgnoreCase);

    // Null when the app never turned bearer on, which is the default. A caller that asks anyway gets the
    // ordinary cookie answer rather than an error: it signed in, and saying otherwise would be a lie.
    private static AuthOptions? BearerFor(HttpContext context) =>
        context.RequestServices.GetService<AuthOptions>() is { Bearer: true } options ? options : null;

    private static CurrentUser Principal(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier),
        user.Identity?.Name,
        user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray());

    /// <summary>A refusal, in the one shape every client already parses.</summary>
    private static IResult Refuse(AuthResult result, int statusCode) =>
        Results.Json(new AuthFailure(result.Error.ToString(), result.Message), statusCode: statusCode);

    // The address a throttle is keyed on. Behind a proxy this is the proxy's address unless the app runs
    // UseForwardedHeaders, which is the ordinary ASP.NET arrangement for learning the client's.
    private static string? Client(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    // Where this app is being served from, used only when the app configured neither PasskeyOrigins nor PublicOrigin.
    // Built from the request's own scheme and host rather than the Origin header: a host is what the server was asked
    // for and what host filtering already governs, while the header is whatever the caller chose to send.
    private static string Origin(HttpContext context) =>
        context.Request.Scheme + "://" + context.Request.Host.Value;

    private static bool HasRequestHeader(HttpContext context) =>
        context.Request.Headers.ContainsKey(RaskAuthDefaults.RequestHeader);

    /// <summary>The answer to a request that did not carry the required header.</summary>
    /// <remarks>
    /// Named for what is actually wrong rather than folded into a credentials failure. The header
    /// requirement is public, documented API — saying so leaks nothing, and a caller that gets
    /// "invalid credentials" for a correct password would have no way to find the real cause.
    /// </remarks>
    private static IResult MissingRequestHeader() =>
        Results.Json(
            new AuthFailure(
                "MissingRequestHeader",
                $"Every request to these endpoints must carry the '{RaskAuthDefaults.RequestHeader}' "
                + "header. A same-origin fetch can set it; cross-site markup cannot, which is what makes "
                + "it a CSRF defence."),
            statusCode: StatusCodes.Status400BadRequest);
}
