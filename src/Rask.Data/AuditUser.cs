using System.Globalization;
using System.Security.Claims;

namespace Rask.Data;

/// <summary>
///     Who an <see cref="AuditFormat{TRow, TUserId}" /> is told did the work: the user as the app's own key type, or null
///     for nobody.
/// </summary>
/// <remarks>
///     An explicit <c>Current.UseUser</c> first — a background job runs for the user its row recorded — then the
///     <see cref="ClaimTypes.NameIdentifier" /> claim of <c>Current.Principal</c>, read as the key type whether or not the
///     app names its user aggregate (<see cref="IUser" />).
/// </remarks>
internal static class AuditUser
{
    /// <summary>Refuses a key type no user is keyed by.</summary>
    internal static void Demand<TUserId>()
        where TUserId : struct
    {
        if (typeof(TUserId) != typeof(Guid) && typeof(TUserId) != typeof(int) && typeof(TUserId) != typeof(long))
        {
            throw new NotSupportedException(
                $"An audit format's user is keyed by a Guid, an int or a long, as the app's users are, not by {typeof(TUserId).Name}: " +
                "class MyAudits : AuditFormat<MyAudit, int>.");
        }
    }

    /// <summary>The user the work in flight is for, as <typeparamref name="TUserId" />; null for nobody.</summary>
    /// <exception cref="InvalidOperationException">The user named is no <typeparamref name="TUserId" />.</exception>
    internal static TUserId? Of<TUserId>()
        where TUserId : struct
    {
        if (CurrentUser.InUserScope(out var scoped))
        {
            return scoped is { } id ? FromGuid<TUserId>(id) : null;
        }

        return Current.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value is { } claim ? FromClaim<TUserId>(claim) : null;
    }

    // The Guid the framework carries a user in: itself, or the number it carries for a numbered app.
    private static TUserId FromGuid<TUserId>(Guid id)
        where TUserId : struct
    {
        if (typeof(TUserId) == typeof(Guid))
        {
            return (TUserId)(object)id;
        }

        return TenantNumber.TryRead(id, out var number) && Narrowed<TUserId>(number) is { } narrowed
            ? narrowed
            : throw Mismatch<TUserId>(id.ToString());
    }

    private static TUserId FromClaim<TUserId>(string claim)
        where TUserId : struct
    {
        if (typeof(TUserId) == typeof(Guid))
        {
            return Guid.TryParse(claim, out var id) ? (TUserId)(object)id : throw Mismatch<TUserId>(claim);
        }

        return long.TryParse(claim, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) &&
               Narrowed<TUserId>(number) is { } narrowed
            ? narrowed
            : throw Mismatch<TUserId>(claim);
    }

    private static TUserId? Narrowed<TUserId>(long number)
        where TUserId : struct
    {
        if (typeof(TUserId) == typeof(long))
        {
            return (TUserId)(object)number;
        }

        return number is >= int.MinValue and <= int.MaxValue ? (TUserId)(object)(int)number : null;
    }

    private static InvalidOperationException Mismatch<TUserId>(string user) =>
        new($"The work in flight is for user '{user}', which is no {typeof(TUserId).Name}, as the audit format's user is keyed. " +
            "Key the format by the type the app's users are: AuditFormat<MyAudit, Guid>, <MyAudit, int> or <MyAudit, long>.");
}
