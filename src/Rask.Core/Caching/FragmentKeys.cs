using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Core.Caching;

/// <summary>
///     The key a <see cref="Cached" /> fragment is stored under: its name, then a hash of everything it varies by.
/// </summary>
/// <remarks>
///     <para>
///         <b>What is always in it.</b> The tenant (the <c>rask:tenant</c> claim, so an administrator acting in a
///         tenant is keyed by it too), the visitor's culture and UI culture — what a fragment formats is in them — and,
///         unless the fragment says <c>.AcrossUsers()</c>, the signed-in user. None of those can be left out by
///         accident: sharing across users is a step someone wrote, and across tenants is not offered at all.
///     </para>
///     <para>
///         <b>When there is no safe key, there is no key.</b> A user whose principal is still loading, a signed-in
///         user with no id to tell them apart, a header asked for with no request to read, a path asked for with no
///         route: each answers null, and the fragment is drawn live and never stored, rather than stored under a key
///         that could hand one visitor's markup to another.
///     </para>
///     <para>
///         The varying values are hashed rather than written into the key, so a distributed cache never holds a user
///         id, a query string or a header in the clear, and a key stays short whatever it varies by. Each value goes in
///         with its length ahead of it, so no two different sets of values can run together into the same text.
///     </para>
/// </remarks>
internal static class FragmentKeys
{
    /// <summary>What every key and tag of a cached fragment starts with, so none collides with the app's own cache entries.</summary>
    internal const string Prefix = "rask:cached:";

    /// <summary>The claim a tenant rides on — the one Rask.Auth stamps and <c>Current.Tenant</c> reads.</summary>
    internal const string TenantClaim = "rask:tenant";

    /// <summary>The tag every entry of the fragment named <paramref name="nameOrTag" /> carries, and what forgetting a tag removes.</summary>
    internal static string Tag(string nameOrTag) => Prefix + nameOrTag;

    /// <summary>The key for <paramref name="fragment" /> in the render now running, or null when it cannot be keyed safely.</summary>
    internal static string? For(Cached fragment, IServiceProvider? services)
    {
        var name = fragment.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "A Cached fragment needs a name to be stored under: write Cached.Name(\"sales-summary\")[ … ].");
        }

        var parts = new StringBuilder(128);
        if (!AppendWho(parts, fragment, services))
        {
            return null;
        }

        // The render's own cultures — the formatting one is the language's country since #1305 ("en" writes en-US money),
        // and both are what the markup was written in.
        var live = Live.LiveRenderContext.Current;
        Append(parts, "c", (live?.Culture ?? CultureInfo.CurrentCulture).Name);
        Append(parts, "ui", (live?.UICulture ?? CultureInfo.CurrentUICulture).Name);

        if (!AppendWhere(parts, fragment, services) || !AppendHeaders(parts, fragment, services))
        {
            return null;
        }

        if (fragment.VaryBy is { } custom)
        {
            Append(parts, "k", Convert.ToString(custom, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return Prefix + name + ":" + Hash(parts);
    }

    // The tenant always; the user unless the fragment is shared across users. False when the user cannot be told apart.
    private static bool AppendWho(StringBuilder parts, Cached fragment, IServiceProvider? services)
    {
        var users = services?.GetService<IUserProvider>();
        if (users is { IsLoading: true })
        {
            return false;
        }

        var principal = users?.Current;
        Append(parts, "t", principal?.FindFirst(TenantClaim)?.Value ?? string.Empty);
        if (fragment.AcrossUsers)
        {
            Append(parts, "u", "*");
            return true;
        }

        if (principal?.Identity is not { IsAuthenticated: true } identity)
        {
            Append(parts, "u", string.Empty);
            return true;
        }

        var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? principal.FindFirst("sub")?.Value
                 ?? identity.Name;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        Append(parts, "u", id);
        return true;
    }

    // The path and the named query values. False when either is asked for and there is no route to read.
    private static bool AppendWhere(StringBuilder parts, Cached fragment, IServiceProvider? services)
    {
        if (!fragment.VaryByRoute && fragment.VaryByQuery is not { Length: > 0 })
        {
            return true;
        }

        if (services?.GetService<RouteState>() is not { } route)
        {
            return false;
        }

        if (fragment.VaryByRoute)
        {
            Append(parts, "p", route.Path);
        }

        foreach (var name in fragment.VaryByQuery ?? [])
        {
            Append(parts, "q", name);
            Append(parts, "v", route.Query.TryGetValue(name, out var values) ? values.ToString() : string.Empty);
        }

        return true;
    }

    // The named request headers. False when one is asked for and there is no request to read.
    private static bool AppendHeaders(StringBuilder parts, Cached fragment, IServiceProvider? services)
    {
        if (fragment.VaryByHeader is not { Length: > 0 } names)
        {
            return true;
        }

        var headers = services?.GetService<IRequestHeaders>();
        foreach (var name in names)
        {
            if (headers?.Header(name) is not { } value)
            {
                return false;
            }

            Append(parts, "h", name.ToLowerInvariant());
            Append(parts, "v", value);
        }

        return true;
    }

    private static void Append(StringBuilder parts, string label, string value) =>
        parts.Append(label).Append(':').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');

    private static string Hash(StringBuilder parts)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString()), digest);
        return Convert.ToHexStringLower(digest);
    }
}
