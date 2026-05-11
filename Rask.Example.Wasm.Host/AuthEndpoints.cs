using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Rask.Example.Wasm.Host;

internal static class AuthEndpoints
{
    private static readonly Dictionary<string, string> _users = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = "password",
        ["bob"] = "password"
    };

    public static async Task<IResult> LoginAsync(HttpContext ctx, [FromBody] LoginRequest request)
    {
        var safeReturnUrl = SafeReturnUrl(request.ReturnUrl);
        var name = request.Username?.Trim() ?? "";
        var pwd = request.Password ?? "";

        if (!_users.TryGetValue(name, out var expected) || expected != pwd)
        {
            return Results.Json(new LoginResponse(false, null), statusCode: StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.NameIdentifier, name)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(false);

        return Results.Ok(new LoginResponse(true, safeReturnUrl));
    }

    public static async Task LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        ctx.Response.Redirect("/");
    }

    public static async Task MeAsync(HttpContext ctx)
    {
        var user = ctx.User;
        var response = user.Identity?.IsAuthenticated == true
            ? new MeResponse(true, user.Identity.Name,
                user.Claims.Select(c => new ClaimDto(c.Type, c.Value)).ToArray())
            : new MeResponse(false, null, []);
        await ctx.Response.WriteAsJsonAsync(response).ConfigureAwait(false);
    }

    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl
            : "/";

    public sealed record LoginRequest(string? Username, string? Password, string? ReturnUrl);

    public sealed record LoginResponse(bool Success, string? RedirectUrl);

    private sealed record MeResponse(bool IsAuthenticated, string? Name, ClaimDto[] Claims);

    public sealed record ClaimDto(string Type, string Value);
}
