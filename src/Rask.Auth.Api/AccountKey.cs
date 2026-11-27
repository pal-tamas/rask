using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>
/// An account's key, and the <see cref="Guid" /> Rask carries it as: a <see cref="Guid" /> is itself, a typed id over a
/// <see cref="Guid" /> (<c>UserId(Guid Value)</c>) is the <see cref="Guid" /> it holds, and a <see cref="string" /> is that
/// <see cref="Guid" /> written out as <c>Guid.ToString()</c> writes it.
/// </summary>
/// <remarks>
/// <para>
/// Every row Rask.Auth keeps about an account — its sessions, its passkeys, the instance claim — and every claim and event
/// names it by that <see cref="Guid" />, which is also what <c>Current.UserId</c> reads. So a typed or a text key is not a
/// second kind of account, only a second spelling of the same one, and the tables Rask.Auth owns are the same whichever
/// the app has: a <c>UserId</c> over a <see cref="Guid" /> is stored as the <see cref="Guid" /> column it replaces.
/// </para>
/// <para>
/// A typed id is crossed by the two functions the generated code hands <see cref="AuthUser.Use{TUser,TKey}(Func{Guid,TKey},Func{TKey,Guid})" />
/// (<c>value =&gt; new UserId(value)</c>, <c>id =&gt; id.Value</c>), so nothing here reflects over it.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The account's key type.</typeparam>
internal static class AccountKey<TKey>
    where TKey : notnull
{
    // EF.Property names the key by its name, so one query serves either key type without a cast inside it.
    private const string KeyProperty = nameof(Authenticatable.Id);

    private static Func<Guid, TKey>? _wrap;
    private static Func<TKey, Guid>? _unwrap;

    /// <summary>Whether <typeparamref name="TKey" /> is a key Rask.Auth can carry.</summary>
    internal static bool IsSupported => typeof(TKey) == typeof(Guid) || typeof(TKey) == typeof(string) || IsTyped;

    /// <summary>Whether <typeparamref name="TKey" /> is a typed id over a <see cref="Guid" /> the generated code named.</summary>
    internal static bool IsTyped => _wrap is not null;

    /// <summary>Makes <typeparamref name="TKey" /> a typed id over a <see cref="Guid" />, crossed by these two functions.</summary>
    /// <param name="wrap">The id holding a value: <c>value =&gt; new UserId(value)</c>.</param>
    /// <param name="unwrap">The value an id holds: <c>id =&gt; id.Value</c>.</param>
    internal static void Typed(Func<Guid, TKey> wrap, Func<TKey, Guid> unwrap)
    {
        _wrap = wrap;
        _unwrap = unwrap;
    }

    /// <summary>Refuses a key Rask.Auth cannot carry, naming the ones it can.</summary>
    /// <param name="user">The account type, for the message.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TKey" /> is none of them.</exception>
    internal static void Require(Type user)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException(
                $"{user.Name} is keyed by a {typeof(TKey).Name}. An account is keyed by a typed id over a Guid — "
                + "Authenticatable<UserId> with `public readonly partial record struct UserId(Guid Value);` — by a Guid — "
                + "Authenticatable — or by a string holding one — Authenticatable<string> — because Rask carries the "
                + "signed-in user as a Guid: Current.UserId, a session's UserId, the events. A typed id is named to "
                + "Rask.Auth by the generated code of the project that declares the user.");
        }
    }

    /// <summary>A new account's key.</summary>
    internal static TKey New() => From(Uuid.Create());

    /// <summary>The <see cref="Guid" /> <paramref name="key" /> carries.</summary>
    /// <exception cref="InvalidOperationException">A text key that holds no <see cref="Guid" />.</exception>
    internal static Guid Of(TKey key) => key switch
    {
        Guid id => id,
        string text when Guid.TryParse(text, out var id) => id,
        _ when _unwrap is { } unwrap => unwrap(key),
        _ => throw new InvalidOperationException(
            $"An account's key '{key}' holds no Guid. A users table keyed by text keeps a Guid in it, as Guid.ToString() "
            + "writes one, because Rask carries the signed-in user as that Guid."),
    };

    /// <summary>The key that carries <paramref name="id" />.</summary>
    internal static TKey From(Guid id)
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return (TKey)(object)id;
        }

        return _wrap is { } wrap ? wrap(id) : (TKey)(object)id.ToString();
    }

    /// <summary>The account carrying <paramref name="id" />, as a query predicate.</summary>
    /// <typeparam name="TUser">The account type.</typeparam>
    internal static Expression<Func<TUser, bool>> Is<TUser>(Guid id)
        where TUser : Authenticatable<TKey>
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return u => EF.Property<Guid>(u, KeyProperty) == id;
        }

        if (IsTyped)
        {
            // The id's own Equals, which EF Core translates to the column's = through the id's converter.
            var key = From(id);
            return u => EF.Property<TKey>(u, KeyProperty).Equals(key);
        }

        var text = id.ToString();
        return u => EF.Property<string>(u, KeyProperty) == text;
    }
}
