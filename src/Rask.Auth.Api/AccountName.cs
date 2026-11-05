using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Auth;

/// <summary>
/// What an account signs in with: its address, or the property its type marks
/// <see cref="SignInNameAttribute">[SignInName]</see> — found once per account type, and read through the column's own
/// mapping, so a value object such as <c>Username</c> is built and read back exactly as its column stores it.
/// </summary>
/// <typeparam name="TUser">The account type.</typeparam>
/// <typeparam name="TKey">Its key type.</typeparam>
internal sealed class AccountName<TUser, TKey>
    where TUser : Authenticatable<TKey>
    where TKey : notnull
{
    /// <summary>The longest name a registration takes, in characters: the address's own limit.</summary>
    internal const int MaxLength = 256;

#pragma warning disable S2743 // one per account type is the point: the declaration and the column are that type's own
    // The attribute is the declaration, read off the app's own type once; a problem is kept and thrown where it can be
    // read (Require, at start-up) rather than from a type initializer, which would hide it in a TypeInitializationException.
    private static readonly (PropertyInfo? Property, string? Problem) Declared = Find();

    // The last model it was resolved against: one per process in practice, so a held answer is a reference comparison.
    private static AccountName<TUser, TKey>? _last;
#pragma warning restore S2743

    private readonly IModel _model;
    private readonly PropertyInfo _property;
    private readonly ValueConverter? _converter;
    private readonly Func<TUser, object?> _read;

    private AccountName(IModel model, PropertyInfo property)
    {
        _model = model;
        _property = property;
        var column = model.FindEntityType(typeof(TUser))?.FindProperty(property.Name);
        _converter = column?.GetValueConverter() ?? column?.GetTypeMapping().Converter;

        if (property.PropertyType != typeof(string) && _converter?.ProviderClrType != typeof(string))
        {
            throw new InvalidOperationException(
                $"{typeof(TUser).Name}.{property.Name} says [SignInName], but it is not stored as text. A sign-in name is a "
                + "string, or a value object holding one string.");
        }

        var user = Expression.Parameter(typeof(TUser), "u");
        _read = Expression.Lambda<Func<TUser, object?>>(
            Expression.Convert(Expression.Property(user, property), typeof(object)), user).Compile();
    }

    /// <summary>Whether the account signs in with its address: no property says <c>[SignInName]</c>.</summary>
    internal static bool IsEmail => Declared.Property is null;

    /// <summary>The property marked <c>[SignInName]</c>, or null for an account that signs in with its address.</summary>
    internal static PropertyInfo? Property => Declared.Property;

    /// <summary>Refuses an account type whose <c>[SignInName]</c> cannot be one.</summary>
    /// <exception cref="InvalidOperationException">Two properties say it, or one that is optional does.</exception>
    internal static void Require()
    {
        if (Declared.Problem is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
    }

    /// <summary>The name as it is compared and stored: an address trimmed and lower-cased, a name trimmed.</summary>
    internal static string Normalize(string typed) =>
        IsEmail ? Authenticatable.NormalizeEmail(typed) : typed.Trim();

    /// <summary>The account <paramref name="normalized" /> names, as a query predicate.</summary>
    internal static Expression<Func<TUser, bool>> Is(DbContext db, string normalized) =>
        IsEmail ? u => u.Email == normalized : In(db.Model).Named(normalized);

    /// <summary>What <paramref name="user" /> signs in with, as text: the principal's name.</summary>
    internal static string Of(IModel model, TUser user) => IsEmail ? user.Email : In(model).Text(user);

    /// <summary>Writes <paramref name="normalized" /> into the new <paramref name="user" />'s sign-in name.</summary>
    /// <remarks>Through the context, which sets a property with a private setter the way it does when it reads a row.</remarks>
    internal static void Write(DbContext db, TUser user, string normalized)
    {
        if (!IsEmail)
        {
            var name = In(db.Model);
            db.Entry(user).Property(name._property.Name).CurrentValue = name.Value(normalized);
        }
    }

    private static AccountName<TUser, TKey> In(IModel model)
    {
        var last = _last;
        if (last is not null && ReferenceEquals(last._model, model))
        {
            return last;
        }

        Require();
        return _last = new AccountName<TUser, TKey>(model, Declared.Property!);
    }

    // u => u.Name == (TName)held.Value — a member of a held box rather than a constant, so EF Core sends it as a
    // parameter: one cached plan for every name, and nothing typed ever spliced into SQL.
    private Expression<Func<TUser, bool>> Named(string normalized)
    {
        var user = Expression.Parameter(typeof(TUser), "u");
        var held = Expression.Field(Expression.Constant(new StrongBox<object?>(Value(normalized))), nameof(StrongBox<object>.Value));
        return Expression.Lambda<Func<TUser, bool>>(
            Expression.Equal(Expression.Property(user, _property), Expression.Convert(held, _property.PropertyType)), user);
    }

    private object? Value(string normalized) => _converter is null ? normalized : _converter.ConvertFromProvider(normalized);

    private string Text(TUser user) =>
        (_converter is null ? _read(user) : _converter.ConvertToProvider(_read(user))) as string ?? "";

    private static (PropertyInfo? Property, string? Problem) Find()
    {
        var marked = typeof(TUser)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static p => p.IsDefined(typeof(SignInNameAttribute), inherit: true))
            .ToList();

        return marked switch
        {
            [] => (null, null),
            [var one] when Nullable.GetUnderlyingType(one.PropertyType) is not null => (null,
                $"{typeof(TUser).Name}.{one.Name} says [SignInName] but is optional. Everyone signs in with one, so it is required."),
            [var one] => (one, null),
            _ => (null,
                $"{typeof(TUser).Name} marks {string.Join(" and ", marked.Select(static p => p.Name))} with [SignInName]. "
                + "An account signs in with one name: mark one of them."),
        };
    }
}
