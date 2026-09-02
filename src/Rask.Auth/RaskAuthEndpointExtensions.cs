using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>Names shared by the auth endpoints and the clients that call them.</summary>
public static class RaskAuthDefaults
{
    /// <summary>
    /// The header every state-changing auth request must carry.
    /// </summary>
    /// <remarks>
    /// A custom header is a CSRF defence that needs no token round-trip: cross-site markup — a form,
    /// an <c>&lt;img&gt;</c>, a <c>&lt;script&gt;</c> — cannot set one, so only a same-origin <c>fetch</c>
    /// reaches these endpoints. It is belt-and-braces over the <c>SameSite=Lax</c> cookie, which already
    /// withholds itself from a cross-site POST; two cheap defences are worth more here than one, because
    /// this is the endpoint that mints a session.
    /// </remarks>
    public const string RequestHeader = "X-Rask-Auth";
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

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        // Cast to Delegate deliberately (ASP0016). LogoutAsync takes only an HttpContext, which is
        // exactly RequestDelegate's shape, so without the cast ASP.NET binds it as one and throws the
        // returned IResult away — the sign-out would happen and the response would not say so.
        group.MapPost("/logout", (Delegate)LogoutAsync);
        group.MapGet("/me", Me);

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
            .RegisterAsync(request.Email, request.Password, request.FirstRunToken, cancellationToken)
            .ConfigureAwait(false);

        return await CompleteAsync(context, outcome).ConfigureAwait(false);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context, LoginRequest request, IAccounts accounts)
    {
        if (!HasRequestHeader(context))
        {
            return MissingRequestHeader();
        }

        var outcome = await accounts.ValidateAsync(request.Email, request.Password).ConfigureAwait(false);
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
            // 401 for every refusal, carrying the code but never a hint about which account exists.
            return Results.Json(
                new AuthFailure(outcome.Result.Error.ToString(), outcome.Result.Message),
                statusCode: StatusCodes.Status401Unauthorized);
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
        return Describe(principal);
    }

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

    /// <summary>Credentials for a new account.</summary>
    /// <param name="Email">The email address, which is also the user name.</param>
    /// <param name="Password">The password.</param>
    /// <param name="FirstRunToken">The first-run token, needed only while no account exists yet.</param>
    public sealed record RegisterRequest(string Email, string Password, string? FirstRunToken = null);

    /// <summary>Credentials for an existing account.</summary>
    /// <param name="Email">The email address.</param>
    /// <param name="Password">The password.</param>
    /// <param name="Remember">Whether the session should outlive the browser session.</param>
    public sealed record LoginRequest(string Email, string Password, bool Remember = false);

    /// <summary>Who is signed in.</summary>
    /// <param name="Id">The account's stable id.</param>
    /// <param name="Email">The account's email address.</param>
    /// <param name="Roles">The roles it holds.</param>
    public sealed record CurrentUser(string? Id, string? Email, IReadOnlyList<string> Roles);

    /// <summary>Why a request was refused.</summary>
    /// <param name="Error">The <see cref="AuthError" /> name.</param>
    /// <param name="Message">A human-readable detail, when there is one.</param>
    public sealed record AuthFailure(string Error, string? Message);
}
