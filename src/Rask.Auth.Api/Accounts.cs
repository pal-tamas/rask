using System.ComponentModel;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>
/// An administrator's hand on the accounts of the tenant they work in, with nothing injected — from an admin page's
/// handler:
/// </summary>
/// <remarks>
/// <code>
/// await Accounts.Create(name, password);
/// await Accounts.Rename(id, name);
/// await Accounts.SetPassword(id, password);
/// await Accounts.Remove(id);
/// </code>
/// <para>
/// Each call is <see cref="IAccounts" />'s, in the work it runs in, and refused unless the signed-in user is an
/// administrator. Outside any work — a hosted service, a timer — it throws; inject <see cref="IAccounts" /> there instead.
/// </para>
/// </remarks>
public static class Accounts
{
    /// <summary>Whether Rask.Auth is registered at all, for an operator surface that renders "off" rather than failing.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn => Ambient.Services?.GetService(typeof(IAccounts)) is not null;

    /// <inheritdoc cref="IAccounts.Create(string, string, CancellationToken)" />
    public static Task<AuthResult> Create(string name, string password, CancellationToken cancellationToken = default) =>
        Resolve().Create(name, password, cancellationToken);

    /// <inheritdoc cref="IAccounts.Create{TUser}(string, string, Action{TUser}, CancellationToken)" />
    public static Task<AuthResult> Create<TUser>(
        string name, string password, Action<TUser> apply, CancellationToken cancellationToken = default)
        where TUser : class =>
        Resolve().Create(name, password, apply, cancellationToken);

    /// <inheritdoc cref="IAccounts.Rename(Guid, string, CancellationToken)" />
    public static Task<AuthResult> Rename(Guid id, string name, CancellationToken cancellationToken = default) =>
        Resolve().Rename(id, name, cancellationToken);

    /// <inheritdoc cref="IAccounts.Rename(string, string, CancellationToken)" />
    public static Task<AuthResult> Rename(string id, string name, CancellationToken cancellationToken = default) =>
        Resolve().Rename(id, name, cancellationToken);

    /// <inheritdoc cref="IAccounts.SetPassword(Guid, string?, CancellationToken)" />
    public static Task<AuthResult> SetPassword(Guid id, string? password, CancellationToken cancellationToken = default) =>
        Resolve().SetPassword(id, password, cancellationToken);

    /// <inheritdoc cref="IAccounts.SetPassword(string, string?, CancellationToken)" />
    public static Task<AuthResult> SetPassword(string id, string? password, CancellationToken cancellationToken = default) =>
        Resolve().SetPassword(id, password, cancellationToken);

    /// <inheritdoc cref="IAccounts.Remove(Guid, CancellationToken)" />
    public static Task Remove(Guid id, CancellationToken cancellationToken = default) =>
        Resolve().Remove(id, cancellationToken);

    /// <inheritdoc cref="IAccounts.Remove(string, CancellationToken)" />
    public static Task Remove(string id, CancellationToken cancellationToken = default) =>
        Resolve().Remove(id, cancellationToken);

    private static IAccounts Resolve() =>
        Ambient.Reach<IAccounts>("Accounts", "Program.cs says c.Auth.Off()", "AddRaskAuth<AppDbContext>()");
}
