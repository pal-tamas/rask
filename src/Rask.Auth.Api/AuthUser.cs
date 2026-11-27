using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth;

/// <summary>
/// The application's account type, found at compile time so nothing has to name it.
/// </summary>
/// <remarks>
/// <para>
/// Rask ships no user class of its own. An app declares one — <c>rask new</c> writes
/// <c>public sealed class User : Authenticatable</c> into <c>Features/Shared</c> — and a source generator emits a
/// <c>[ModuleInitializer]</c> calling <see cref="Use{TUser}" />, so <c>AddRaskAuth()</c> and
/// <c>modelBuilder.AddRaskAuth()</c> work with no type argument and no registration.
/// </para>
/// <para>
/// Generated, not reflected: the binding closes over the user type at compile time, so nothing scans an
/// assembly and a trimmed publish cannot lose the account tables.
/// </para>
/// <para>
/// An app that declares no user type has no accounts, and the auth battery is simply not wired — which
/// is the honest outcome, rather than mapping tables for a user that does not exist.
/// </para>
/// </remarks>
public static class AuthUser
{
    /// <summary>The declared user type, or <c>null</c> when the app has none.</summary>
    public static Type? Type => Binding?.UserType;

    /// <summary>Whether the app declared a user type.</summary>
    public static bool Exists => Binding is not null;

    internal static IAuthUserBinding? Binding { get; private set; }

    /// <summary>Names the application's user type. Called by generated code.</summary>
    /// <typeparam name="TUser">The application's <see cref="Authenticatable" /> user.</typeparam>
    public static void Use<TUser>()
        where TUser : Authenticatable, new() =>
        Use<TUser, Guid>();

    /// <summary>Names the application's user type and its key. Called by generated code.</summary>
    /// <typeparam name="TUser">The application's <see cref="Authenticatable{TKey}" /> user.</typeparam>
    /// <typeparam name="TKey">
    /// Its key: a <see cref="Guid" />, or a <see cref="string" /> holding one. A typed id over a <see cref="Guid" /> is
    /// named with <see cref="Use{TUser,TKey}(Func{Guid,TKey},Func{TKey,Guid})" />.
    /// </typeparam>
    public static void Use<TUser, TKey>()
        where TUser : Authenticatable<TKey>, new()
        where TKey : notnull =>
        Binding = new UserBinding<TUser, TKey>();

    /// <summary>Names the application's user type keyed by a typed id over a <see cref="Guid" />. Called by generated code.</summary>
    /// <typeparam name="TUser">The application's <see cref="Authenticatable{TKey}" /> user.</typeparam>
    /// <typeparam name="TKey">Its key: a typed id over a <see cref="Guid" /> — <c>UserId(Guid Value)</c>.</typeparam>
    /// <param name="wrap">The id holding a value: <c>value =&gt; new UserId(value)</c>.</param>
    /// <param name="unwrap">The value an id holds: <c>id =&gt; id.Value</c>.</param>
    /// <remarks>
    /// Rask carries the signed-in user as the <see cref="Guid" /> the id holds — its sessions, passkeys, claims and events
    /// — so the id's column is the <see cref="Guid" /> one it replaces, and moving a users table from <see cref="Guid" />
    /// to a <c>UserId</c> over one changes no column.
    /// </remarks>
    public static void Use<TUser, TKey>(Func<Guid, TKey> wrap, Func<TKey, Guid> unwrap)
        where TUser : Authenticatable<TKey>, new()
        where TKey : struct
    {
        ArgumentNullException.ThrowIfNull(wrap);
        ArgumentNullException.ThrowIfNull(unwrap);
        AccountKey<TKey>.Typed(wrap, unwrap);
        Use<TUser, TKey>();
    }

    /// <summary>Forgets the declared user type. Test seam.</summary>
    public static void Reset() => Binding = null;

    // A generic METHOD on a non-generic interface is what makes this work with no reflection: TUser is
    // closed by the generated call, TContext stays open for the caller, and the compiler emits both.
    private sealed class UserBinding<TUser, TKey> : IAuthUserBinding
        where TUser : Authenticatable<TKey>, new()
        where TKey : notnull
    {
        public Type UserType => typeof(TUser);

        public ModelBuilder Map(ModelBuilder modelBuilder) => modelBuilder.AddRaskAuth<TUser, TKey>();

        public IServiceCollection Add<TContext>(IServiceCollection services, Action<AuthOptions>? configure)
            where TContext : DbContext =>
            services.AddRaskAuth<TContext, TUser, TKey>(configure);
    }
}
