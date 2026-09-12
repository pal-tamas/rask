using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth;

/// <summary>
/// The application's account type, found at compile time so nothing has to name it.
/// </summary>
/// <remarks>
/// <para>
/// Rask ships no user class of its own. An app declares one — <c>rask new</c> writes
/// <c>public class User : IdentityUser</c> into <c>Features/Shared</c> — and a source generator emits a
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
    /// <typeparam name="TUser">The application's <see cref="IdentityUser" />.</typeparam>
    public static void Use<TUser>()
        where TUser : IdentityUser, new() =>
        Binding = new UserBinding<TUser>();

    /// <summary>Forgets the declared user type. Test seam.</summary>
    public static void Reset() => Binding = null;

    // A generic METHOD on a non-generic interface is what makes this work with no reflection: TUser is
    // closed by the generated call, TContext stays open for the caller, and the compiler emits both.
    private sealed class UserBinding<TUser> : IAuthUserBinding
        where TUser : IdentityUser, new()
    {
        public Type UserType => typeof(TUser);

        public ModelBuilder Map(ModelBuilder modelBuilder) => modelBuilder.AddRaskAuth<TUser>();

        public IServiceCollection Add<TContext>(IServiceCollection services, Action<AuthOptions>? configure)
            where TContext : DbContext =>
            services.AddRaskAuth<TContext, TUser>(configure);
    }
}

internal interface IAuthUserBinding
{
    Type UserType { get; }

    ModelBuilder Map(ModelBuilder modelBuilder);

    IServiceCollection Add<TContext>(IServiceCollection services, Action<AuthOptions>? configure)
        where TContext : DbContext;
}
