using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
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

    // EF Core's own precision for a decimal column, kept when a value object declares only its scale.
    private const int DefaultPrecision = 18;

    /// <summary>Stores <paramref name="property" /> as the one value <paramref name="value" /> reads from it.</summary>
    /// <typeparam name="TValueObject">The value object's type.</typeparam>
    /// <typeparam name="TValue">The type of the value it holds, which is the column's.</typeparam>
    /// <param name="property">The entity's property that holds the value object.</param>
    /// <param name="value">Reads the one value — <c>v =&gt; v.Value</c>.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="InvalidOperationException">The value object cannot be built from its value.</exception>
    public static PropertyBuilder Map<[DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(
        PropertyBuilder property, Expression<Func<TValueObject, TValue>> value) =>
        Map(property, value, default);

    /// <summary>
    ///     Stores <paramref name="property" /> as the one value <paramref name="value" /> reads from it, in a column
    ///     shaped by what the value object declares.
    /// </summary>
    /// <typeparam name="TValueObject">The value object's type.</typeparam>
    /// <typeparam name="TValue">The type of the value it holds, which is the column's.</typeparam>
    /// <param name="property">The entity's property that holds the value object.</param>
    /// <param name="value">Reads the one value — <c>v =&gt; v.Value</c>.</param>
    /// <param name="facets">The length, precision and scale it declares — the generated <c>X.__Facets</c>.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="InvalidOperationException">The value object cannot be built from its value.</exception>
    public static PropertyBuilder Map<[DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(
        PropertyBuilder property, Expression<Func<TValueObject, TValue>> value, ValueFacets facets)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        var (converter, comparer) = Conversion(value);
        property.HasConversion(converter, comparer);

        Declare(facets, length => property.HasMaxLength(length), (precision, scale) => property.HasPrecision(precision, scale));

        return property;
    }

    /// <summary>
    ///     Stores <paramref name="property" /> — held inside another value object — as the one value
    ///     <paramref name="value" /> reads from it.
    /// </summary>
    /// <typeparam name="TValueObject">The value object's type.</typeparam>
    /// <typeparam name="TValue">The type of the value it holds, which is the column's.</typeparam>
    /// <param name="property">The outer value object's property that holds the value object.</param>
    /// <param name="value">Reads the one value — <c>v =&gt; v.Value</c>.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="InvalidOperationException">The value object cannot be built from its value.</exception>
    public static ComplexTypePropertyBuilder Map<[DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(
        ComplexTypePropertyBuilder property, Expression<Func<TValueObject, TValue>> value) =>
        Map(property, value, default);

    /// <summary>
    ///     Stores <paramref name="property" /> — held inside another value object — as the one value
    ///     <paramref name="value" /> reads from it, in a column shaped by what the value object declares.
    /// </summary>
    /// <typeparam name="TValueObject">The value object's type.</typeparam>
    /// <typeparam name="TValue">The type of the value it holds, which is the column's.</typeparam>
    /// <param name="property">The outer value object's property that holds the value object.</param>
    /// <param name="value">Reads the one value — <c>v =&gt; v.Value</c>.</param>
    /// <param name="facets">The length, precision and scale it declares — the generated <c>X.__Facets</c>.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="InvalidOperationException">The value object cannot be built from its value.</exception>
    public static ComplexTypePropertyBuilder Map<[DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(
        ComplexTypePropertyBuilder property, Expression<Func<TValueObject, TValue>> value, ValueFacets facets)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        var (converter, comparer) = Conversion(value);
        property.HasConversion(converter, comparer);

        Declare(facets, length => property.HasMaxLength(length), (precision, scale) => property.HasPrecision(precision, scale));

        return property;
    }

    /// <summary>
    ///     Names <paramref name="property" />'s column as the complex mapping named it — <c>Price_Currency</c> — at
    ///     convention strength, so the app's <c>ColumnNames</c> renames it as it renames its siblings. Called by generated
    ///     code.
    /// </summary>
    /// <param name="property">A one-value value object's column inside another value object.</param>
    /// <param name="column">The column EF Core's complex mapping would give it.</param>
    /// <returns>The same builder.</returns>
    public static ComplexTypePropertyBuilder Held(ComplexTypePropertyBuilder property, string column)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentException.ThrowIfNullOrEmpty(column);

        ((IConventionProperty)property.Metadata).Builder.HasColumnName(column);
        return property;
    }

    // The length, precision and scale a value object declared from a base record states, read when the app was
    // compiled — building one to ask would check its rules. The entity's own Configure runs after this, so a
    // HasMaxLength there still has the last word (RASK128 says when it disagrees).
    private static void Declare(ValueFacets facets, Action<int> length, Action<int, int> precision)
    {
        if (facets.MaxLength is > 0 and var most)
        {
            length(most);
        }

        if (facets.Precision is { } digits)
        {
            precision(digits, facets.Scale ?? 0);
        }
        else if (facets.Scale is { } scale)
        {
            precision(DefaultPrecision, scale);
        }
    }

    private static (ValueConverter<TValueObject, TValue> Converter, ValueComparer<TValueObject> Comparer) Conversion<
        [DynamicallyAccessedMembers(Kept)] TValueObject, TValue>(Expression<Func<TValueObject, TValue>> value)
    {
        var build = Loaded(Build<TValueObject, TValue>(value));
        var read = value.Compile();
        var rebuild = build.Compile();

        return (
            OneValueConverters.Create(value, build),
            new ValueComparer<TValueObject>(
                (a, b) => Same(read, a, b),
                v => Hash(read, v),
                v => Copy(read, rebuild, v)));
    }

    // A value object that checks its rules as it is made (Rask.Data.Value) is rebuilt from its column inside a loading
    // scope, so a row stored before a rule was tightened still loads, and a snapshot of it still copies. An expression
    // tree cannot hold a `using`, so the build is a call to a method that does.
    private static Expression<Func<TValue, TValueObject>> Loaded<TValueObject, TValue>(Expression<Func<TValue, TValueObject>> build)
    {
        if (!typeof(Value).IsAssignableFrom(typeof(TValueObject)))
        {
            return build;
        }

        var raw = build.Compile();
        return value => Load(raw, value);
    }

    private static TValueObject Load<TValueObject, TValue>(Func<TValue, TValueObject> build, TValue value)
    {
        using (Loading.Begin())
        {
            return build(value);
        }
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
