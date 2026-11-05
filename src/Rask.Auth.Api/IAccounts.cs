using Rask.Wire;

namespace Rask.Auth;

/// <summary>
/// An administrator's hand on the accounts of the tenant they work in: create one, rename it, set its password,
/// remove it — with nobody signed in or out but the user it is about.
/// </summary>
/// <remarks>
/// <para>
/// For an admin page. Each call is the signed-in user's, and refused with a <c>Rask.Cqrs.ForbiddenException</c>
/// unless their row holds <see cref="RaskRoles.Admin" /> — checked here as well as by the page's
/// <c>[Authorize(Roles = "admin")]</c>, so a page that forgot its gate does not hand the accounts to anybody.
/// </para>
/// <para>
/// It reaches the accounts of the tenant in flight (<c>Current.Tenant</c>: the administrator's choice,
/// <c>auth.ActInTenant(id)</c>, or <c>Tenant.Use(id)</c>) and never an administrator's: another tenant's user, an
/// account that holds <see cref="RaskRoles.Admin" /> and an id nobody has are one answer, a
/// <see cref="KeyNotFoundException" />, and nothing is changed. A new account belongs to the tenant in flight, or to
/// none in an app without tenants.
/// </para>
/// <para>
/// A name is checked as registering checks it — trimmed, and taken while any account holds it, in any tenant, since
/// signing in finds an account before it knows the tenant — and a password by the same policy
/// (<see cref="AuthOptions.MinimumPasswordLength" />, and the hasher's). Both refusals are an <see cref="AuthResult" />:
/// <see cref="AuthError.DuplicateAccount" />, <see cref="AuthError.InvalidEmail" />, <see cref="AuthError.WeakPassword" />.
/// </para>
/// <example>
/// <code>
/// var created = await Accounts.Create(form.Name, form.Password);   // AuthError.DuplicateAccount when the name is taken
/// await Accounts.Rename(id, form.Name);
/// await Accounts.SetPassword(id, form.Password);                  // blank leaves it; otherwise their sign-ins end
/// await Accounts.Remove(id);                                      // with their sessions and passkeys
/// </code>
/// </example>
/// </remarks>
public interface IAccounts
{
    /// <summary>Creates an account in the tenant in flight. Nobody is signed in to it, or out of anything.</summary>
    /// <param name="name">
    /// What it signs in with: the address, or the name for an account whose type marks one <c>[SignInName]</c>.
    /// </param>
    /// <param name="password">Its password, held only long enough to hash it.</param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>
    /// Success, or why not: the name is taken (<see cref="AuthError.DuplicateAccount" />) or not one
    /// (<see cref="AuthError.InvalidEmail" />), or the password is refused (<see cref="AuthError.WeakPassword" />).
    /// </returns>
    /// <remarks>
    /// It holds <see cref="RaskRoles.User" />. An address is sent the link that confirms it, as registering sends one.
    /// </remarks>
    /// <exception cref="Rask.Cqrs.ForbiddenException">The signed-in user is not an administrator, or nobody is signed in.</exception>
    Task<AuthResult> Create(string name, string password, CancellationToken cancellationToken = default);

    /// <summary>Creates an account in the tenant in flight, and sets the app's own columns on it before it is saved.</summary>
    /// <typeparam name="TUser">The app's user type.</typeparam>
    /// <param name="name">What it signs in with: the address, or the <c>[SignInName]</c> name.</param>
    /// <param name="password">Its password, held only long enough to hash it.</param>
    /// <param name="apply">Sets values on the new user: <c>(User u) =&gt; u.Rename(fullName)</c>. Its id is set already.</param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>Success, or why not, as <see cref="Create(string, string, CancellationToken)" /> answers.</returns>
    /// <exception cref="Rask.Cqrs.ForbiddenException">The signed-in user is not an administrator, or nobody is signed in.</exception>
    /// <exception cref="ArgumentException">The app's user type is not a <typeparamref name="TUser" />.</exception>
    Task<AuthResult> Create<TUser>(
        string name, string password, Action<TUser> apply, CancellationToken cancellationToken = default)
        where TUser : class;

    /// <summary>Changes what an account signs in with. The account stays signed in.</summary>
    /// <param name="id">The account's id.</param>
    /// <param name="name">Its new address or name. An address is unconfirmed again, and sent the link that confirms it.</param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>
    /// Success, or why not: another account holds the name (<see cref="AuthError.DuplicateAccount" />) or it is not one
    /// (<see cref="AuthError.InvalidEmail" />).
    /// </returns>
    /// <exception cref="KeyNotFoundException">No account of the tenant in flight has that id, or it is an administrator's.</exception>
    /// <exception cref="Rask.Cqrs.ForbiddenException">The signed-in user is not an administrator, or nobody is signed in.</exception>
    Task<AuthResult> Rename(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="Rename(Guid, string, CancellationToken)" />, for an account keyed by text:
    /// <c>Authenticatable&lt;string&gt;</c>'s <c>Id</c>.
    /// </summary>
    /// <param name="id">The account's id, as its key holds it.</param>
    /// <param name="name">Its new address or name.</param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>Success, or why not.</returns>
    /// <exception cref="KeyNotFoundException">No account of the tenant in flight has that id, or it is an administrator's.</exception>
    Task<AuthResult> Rename(string id, string name, CancellationToken cancellationToken = default) =>
        Rename(Key(id), name, cancellationToken);

    /// <summary>
    /// Gives an account a new password, without the old one — and ends every sign-in it has, so it signs in again with
    /// the new one. A blank password changes nothing.
    /// </summary>
    /// <param name="id">The account's id.</param>
    /// <param name="password">The new password, or <see langword="null" /> or blank to leave the one it has.</param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>Success, or <see cref="AuthError.WeakPassword" /> and why.</returns>
    /// <exception cref="KeyNotFoundException">No account of the tenant in flight has that id, or it is an administrator's.</exception>
    /// <exception cref="Rask.Cqrs.ForbiddenException">The signed-in user is not an administrator, or nobody is signed in.</exception>
    Task<AuthResult> SetPassword(Guid id, string? password, CancellationToken cancellationToken = default);

    /// <summary><see cref="SetPassword(Guid, string?, CancellationToken)" />, for an account keyed by text.</summary>
    /// <param name="id">The account's id, as its key holds it.</param>
    /// <param name="password">The new password, or <see langword="null" /> or blank to leave the one it has.</param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>Success, or <see cref="AuthError.WeakPassword" /> and why.</returns>
    /// <exception cref="KeyNotFoundException">No account of the tenant in flight has that id, or it is an administrator's.</exception>
    Task<AuthResult> SetPassword(string id, string? password, CancellationToken cancellationToken = default) =>
        SetPassword(Key(id), password, cancellationToken);

    /// <summary>Deletes an account with every way in it had — its sessions and its passkeys — in one transaction.</summary>
    /// <param name="id">The account's id.</param>
    /// <param name="cancellationToken">Cancels the delete; unset, the work this call belongs to does.</param>
    /// <returns>A task that completes once the account is gone.</returns>
    /// <remarks>Inside a <c>Db.Transaction</c> it joins it, and is rolled back with it.</remarks>
    /// <exception cref="KeyNotFoundException">No account of the tenant in flight has that id, or it is an administrator's.</exception>
    /// <exception cref="Rask.Cqrs.ForbiddenException">The signed-in user is not an administrator, or nobody is signed in.</exception>
    Task Remove(Guid id, CancellationToken cancellationToken = default);

    /// <summary><see cref="Remove(Guid, CancellationToken)" />, for an account keyed by text.</summary>
    /// <param name="id">The account's id, as its key holds it.</param>
    /// <param name="cancellationToken">Cancels the delete; unset, the work this call belongs to does.</param>
    /// <returns>A task that completes once the account is gone.</returns>
    /// <exception cref="KeyNotFoundException">No account of the tenant in flight has that id, or it is an administrator's.</exception>
    Task Remove(string id, CancellationToken cancellationToken = default) => Remove(Key(id), cancellationToken);

    // A text key holds a Guid; one that holds none is no account's.
    private static Guid Key(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Guid.TryParse(id, out var key) ? key : throw AccountNotFound.For(id);
    }
}
