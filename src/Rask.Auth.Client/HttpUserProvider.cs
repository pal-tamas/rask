using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Auth.Client;

/// <summary>
/// Who is signed in, as the app's own <c>/api/auth/me</c> endpoint reports it.
/// </summary>
/// <remarks>
/// <para>
/// This is the piece a WebAssembly app used to have to write for itself. The documented pattern was to
/// copy an <c>ApiUserProvider</c> out of a sample; shipping it means the browser host reads the current
/// user through the same <see cref="IUserProvider" /> the server host does, and a component cannot tell
/// which one it is running on.
/// </para>
/// <para>
/// Identity travels as a same-origin cookie, so nothing is held in JavaScript and there is no token for
/// a script on the page to read. The request carries no credentials of its own — the browser attaches
/// the cookie because the call is same-origin.
/// </para>
/// </remarks>
public sealed class HttpUserProvider(HttpClient http, AuthClientOptions options) : IUserProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private ClaimsPrincipal _current = Anonymous;
    private bool _loaded;

    /// <inheritdoc />
    public ClaimsPrincipal Current => _current;

    /// <inheritdoc />
    public bool IsLoading { get; private set; }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    /// <remarks>
    /// The browser host awaits this before its first render, which is what stops a page painting
    /// anonymous and then flipping to signed-in a moment later.
    /// </remarks>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        await RefreshAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RefreshAsync()
    {
        IsLoading = true;

        try
        {
            var response = await http
                .GetAsync(options.Prefix + AuthApi.Me)
                .ConfigureAwait(false);

            // 204 is the endpoint's way of saying "nobody", and it is a perfectly good answer rather
            // than a failure — see the remarks on the endpoint itself.
            var user = response.StatusCode is HttpStatusCode.NoContent || !response.IsSuccessStatusCode
                ? null
                : await response.Content.ReadFromJsonAsync<CurrentUser>().ConfigureAwait(false);

            Set(user is null ? Anonymous : Principal(user));
        }
        catch (HttpRequestException)
        {
            // Offline, or the endpoint is unreachable. Anonymous is the safe reading: it closes doors
            // rather than opening them, and a later refresh recovers.
            Set(Anonymous);
        }
        finally
        {
            IsLoading = false;
            _loaded = true;
        }
    }

    private void Set(ClaimsPrincipal user)
    {
        if (ReferenceEquals(_current, user))
        {
            return;
        }

        _current = user;
        Changed?.Invoke();
    }

    /// <summary>Rebuilds the principal the server described.</summary>
    /// <remarks>
    /// The authentication type is non-empty on purpose: <c>ClaimsIdentity.IsAuthenticated</c> is
    /// <c>false</c> for an identity constructed without one, so every <c>[Authorize]</c> check and every
    /// <c>Authorize</c> component would treat a signed-in visitor as anonymous.
    /// </remarks>
    private static ClaimsPrincipal Principal(CurrentUser user)
    {
        var claims = new List<Claim>();

        if (user.Id is { Length: > 0 } id)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        }

        if (user.Email is { Length: > 0 } email)
        {
            claims.Add(new Claim(ClaimTypes.Name, email));
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Rask.Auth"));
    }
}
