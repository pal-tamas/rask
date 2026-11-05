using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Auth;

/// <summary>
/// An account's key, and the <see cref="Guid" /> Rask carries it as: a <see cref="Guid" /> is itself, and a
/// <see cref="string" /> is that <see cref="Guid" /> written out as <c>Guid.ToString()</c> writes it.
/// </summary>
/// <remarks>
/// Every row Rask.Auth keeps about an account — its sessions, its passkeys, the instance claim — and every claim and event
/// names it by that <see cref="Guid" />, which is also what <c>Current.UserId</c> reads. So a text key is not a second kind
/// of account, only a second spelling of the same one, and the tables Rask.Auth owns are the same whichever the app has.
/// </remarks>
/// <typeparam name="TKey">The account's key type.</typeparam>
internal static class AccountKey<TKey>
    where TKey : notnull
{
    // EF.Property names the key by its name, so one query serves either key type without a cast inside it.
    private const string KeyProperty = nameof(Authenticatable.Id);

    /// <summary>Whether <typeparamref name="TKey" /> is a key Rask.Auth can carry.</summary>
    internal static bool IsSupported => typeof(TKey) == typeof(Guid) || typeof(TKey) == typeof(string);

    /// <summary>Refuses a key Rask.Auth cannot carry, naming the two it can.</summary>
    /// <param name="user">The account type, for the message.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TKey" /> is neither a Guid nor a string.</exception>
    internal static void Require(Type user)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException(
                $"{user.Name} is keyed by a {typeof(TKey).Name}. An account is keyed by a Guid — Authenticatable — or by "
                + "a string holding one — Authenticatable<string> — because Rask carries the signed-in user as a Guid: "
                + "Current.UserId, a session's UserId, the events.");
        }
    }

    /// <summary>A new account's key.</summary>
    internal static TKey New() => From(Guid.CreateVersion7());

    /// <summary>The <see cref="Guid" /> <paramref name="key" /> carries.</summary>
    /// <exception cref="InvalidOperationException">A text key that holds no <see cref="Guid" />.</exception>
    internal static Guid Of(TKey key) => key switch
    {
        Guid id => id,
        string text when Guid.TryParse(text, out var id) => id,
        _ => throw new InvalidOperationException(
            $"An account's key '{key}' holds no Guid. A users table keyed by text keeps a Guid in it, as Guid.ToString() "
            + "writes one, because Rask carries the signed-in user as that Guid."),
    };

    /// <summary>The key that carries <paramref name="id" />.</summary>
    internal static TKey From(Guid id) =>
        typeof(TKey) == typeof(Guid) ? (TKey)(object)id : (TKey)(object)id.ToString();

    /// <summary>The account carrying <paramref name="id" />, as a query predicate.</summary>
    /// <typeparam name="TUser">The account type.</typeparam>
    internal static Expression<Func<TUser, bool>> Is<TUser>(Guid id)
        where TUser : Authenticatable<TKey>
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return u => EF.Property<Guid>(u, KeyProperty) == id;
        }

        var text = id.ToString();
        return u => EF.Property<string>(u, KeyProperty) == text;
    }
}
