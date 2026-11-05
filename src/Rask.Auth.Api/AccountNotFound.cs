namespace Rask.Auth;

/// <summary>
/// The one answer <see cref="IAccounts" /> gives for an account it may not touch: none has that id, it is another
/// tenant's, or it is an administrator's. One spelling, so which of the three it was is not told.
/// </summary>
internal static class AccountNotFound
{
    internal static KeyNotFoundException For(object id) =>
        new($"No account '{id}' in the tenant being worked in that Accounts may manage. Another tenant's is reached by "
            + "acting in that tenant first — auth.ActInTenant(tenantId) — and an administrator's is never managed here.");
}
