using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Maps a value object that holds ONE value as the column of that value. Called by generated code.
/// </summary>
/// <remarks>
///     <para>
///         <c>public EmailAddress Email { get; private set; }</c> is stored as the <c>Email</c> column holding the
///         address — an ordinary scalar column with a conversion, so it can be indexed, made unique and given a
///         length like any other: <c>builder.HasIndex(c =&gt; c.Email).IsUnique("…")</c>.
///     </para>
///     <para>
///         The value object is rebuilt the way it is declared: through the constructor that takes its one
///         value, or — for the private-constructor, private-setter shape — a parameterless constructor and the
///         property itself. Two values are compared by what they hold, and a snapshot is a rebuilt copy, so a
///         class that is not a record, and one changed in place, are both tracked correctly.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ValueObjectColumn
{
    // What building a value object reads (its constructors, the property, the field behind it) and what EF Core's
    // comparer asks to be kept.
    private const DynamicallyAccessedMemberTypes Kept = DataTrimming.Entity | DynamicallyAccessedMemberTypes.PublicMethods;

    /// <summary>Stores <paramref name="property" /> as the one value <paramref name="value" /> reads from it.</summary>
    /// <typeparam name="TValueObject">The value object's type.</typeparam>
    /// <typeparam name="TValue">The type of the value it holds, which is the column's.</typeparam>
    /// <param name="property">The entity's property that holds the value object.</param>
    /// <param name="value">Reads the one value — <c>v =&gt; v.Value</c>.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="InvalidOperationException">The value object cannot be built from its value.</exception>
    public static PropertyBuilder Map<[DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(
        PropertyBuilder property, Expression<Func<TValueObject, TValue>> value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        var build = Build<TValueObject, TValue>(value);
        var read = value.Compile();
        var rebuild = build.Compile();

        return property.HasConversion(
            new ValueConverter<TValueObject, TValue>(value, build),
            new ValueComparer<TValueObject>(
                (a, b) => Same(read, a, b),
                v => Hash(read, v),
                v => Copy(read, rebuild, v)));
    }

    private static bool Same<TValueObject, TValue>(Func<TValueObject, TValue> read, TValueObject? a, TValueObject? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return EqualityComparer<TValue>.Default.Equals(read(a), read(b));
    }

    private static int Hash<TValueObject, TValue>(Func<TValueObject, TValue> read, TValueObject held) =>
        held is null || read(held) is not { } value ? 0 : EqualityComparer<TValue>.Default.GetHashCode(value);

    // A rebuilt copy, so a value object changed in place still differs from what was read.
    private static TValueObject Copy<TValueObject, TValue>(
        Func<TValueObject, TValue> read, Func<TValue, TValueObject> rebuild, TValueObject held) =>
        held is null ? held : rebuild(read(held));

    // value => new T(value), or value => new T { Value = value } for a type built through its members.
    private static Expression<Func<TValue, TValueObject>> Build<[DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(
        Expression<Func<TValueObject, TValue>> value)
    {
#pragma warning disable S3011 // a value object's constructor and setter are private by design (RASK084); EF Core binds them the same way
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
#pragma warning restore S3011

        var type = typeof(TValueObject);
        var given = Expression.Parameter(typeof(TValue), "value");

        if (type.GetConstructor(Any, [typeof(TValue)]) is { } fromValue)
        {
            return Expression.Lambda<Func<TValue, TValueObject>>(Expression.New(fromValue, given), given);
        }

        if (value.Body is MemberExpression { Member: PropertyInfo held } &&
            (type.IsValueType || type.GetConstructor(Any, Type.EmptyTypes) is not null) &&
            Writable(type, held, Any) is { } member)
        {
            var empty = type.IsValueType ? Expression.New(type) : Expression.New(type.GetConstructor(Any, Type.EmptyTypes)!);
            return Expression.Lambda<Func<TValue, TValueObject>>(
                Expression.MemberInit(empty, Expression.Bind(member, given)), given);
        }

        throw new InvalidOperationException(
            $"'{type.Name}' holds one value, so it is stored as that value's column, but it cannot be built back " +
            $"from a {typeof(TValue).Name}: it has neither a constructor taking one, nor a parameterless " +
            "constructor with a property that can be set. Give it either — both may be private.");
    }

    // The property when it has a setter of any visibility, else the field behind a get-only auto-property.
    private static MemberInfo? Writable(
        [DynamicallyAccessedMembers(Kept)] Type type, PropertyInfo held, BindingFlags any) =>
        held.SetMethod is not null ? held : type.GetField($"<{held.Name}>k__BackingField", any);
}
