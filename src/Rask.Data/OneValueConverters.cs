using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     The converters Rask gives a value object of one value, and the way back from that value to the object —
///     which is what lets a read face, holding the bare value, read through a converter the app put in their place.
/// </summary>
internal static class OneValueConverters
{
    private static readonly ConcurrentDictionary<Type, ValueConverter> FromValue = new();

    /// <summary>The converter that stores the value object as the one value it holds.</summary>
    public static ValueConverter<TValueObject, TValue> Create<TValueObject, TValue>(
        Expression<Func<TValueObject, TValue>> value, Expression<Func<TValue, TValueObject>> build)
    {
        FromValue.TryAdd(typeof(TValueObject), new ValueConverter<TValue, TValueObject>(build, value));
        return new Own<TValueObject, TValue>(value, build, stored: null);
    }

    /// <summary>
    ///     How a face holding the value reads a column the write side stores through <paramref name="written" />:
    ///     what the provider stores the value as for Rask's own converter (nothing, where the column holds the value
    ///     itself), else the value rebuilt into the value object and stored the app's way.
    /// </summary>
    public static ValueConverter? ForValue(ValueConverter written)
    {
        if (written is IOwn own)
        {
            return own.Stored;
        }

        var valueObject = Nullable.GetUnderlyingType(written.ModelClrType) ?? written.ModelClrType;
        return FromValue.TryGetValue(valueObject, out var fromValue) ? fromValue.ComposeWith(written) : null;
    }

    /// <summary>
    ///     Whether <paramref name="converter" /> is Rask's own, storing its value object as the one value
    ///     <paramref name="member" /> reads — so a query that reads that member reads the column itself.
    /// </summary>
    public static bool StoresAs(ValueConverter? converter, MemberInfo member) =>
        converter is IOwn { Held: { } held } && held.HasSameMetadataDefinitionAs(member);

    /// <summary>
    ///     How the provider stores the value Rask's own <paramref name="converter" /> reads from its value object — a
    ///     wall-clock moment as UTC on PostgreSQL — or null where the column holds the value as it is.
    /// </summary>
    public static ValueConverter? StoredAs(ValueConverter? converter) => (converter as IOwn)?.Stored;

    /// <summary>
    ///     <paramref name="written" />, then <paramref name="stored" /> on the way to the column: Rask's own converter
    ///     stays Rask's own — its <c>.Value</c> still reads the column, through <paramref name="stored" /> — and an
    ///     app's is composed with it.
    /// </summary>
    public static ValueConverter Then(ValueConverter written, ValueConverter stored) =>
        written is IOwn own ? own.StoringThrough(stored) : written.ComposeWith(stored);

    private interface IOwn
    {
        /// <summary>The member the converter reads the value from — <c>v.Value</c> — or null for a computed one.</summary>
        MemberInfo? Held { get; }

        /// <summary>What the provider stores the value as, or null for the value itself.</summary>
        ValueConverter? Stored { get; }

        /// <summary>The same converter, storing the value through <paramref name="stored" />.</summary>
        ValueConverter StoringThrough(ValueConverter stored);
    }

    private sealed class Own<TValueObject, TValue>(
        Expression<Func<TValueObject, TValue>> value, Expression<Func<TValue, TValueObject>> build, ValueConverter? stored)
        : ValueConverter<TValueObject, TValue>(Through(value, stored), Back(build, stored)), IOwn
    {
        public MemberInfo? Held { get; } =
            value.Body is MemberExpression { Member: var member } access && access.Expression == value.Parameters[0] ? member : null;

        public ValueConverter? Stored { get; } = stored;

        public ValueConverter StoringThrough(ValueConverter stored) =>
            new Own<TValueObject, TValue>(value, build, Stored is null ? stored : Stored.ComposeWith(stored));

        // v => stored(read(v)): the value read from the object, then stored the provider's way.
        private static Expression<Func<TValueObject, TValue>> Through(Expression<Func<TValueObject, TValue>> read, ValueConverter? stored) =>
            stored is null
                ? read
                : Expression.Lambda<Func<TValueObject, TValue>>(
                    ReplacingExpressionVisitor.Replace(
                        stored.ConvertToProviderExpression.Parameters[0], read.Body, stored.ConvertToProviderExpression.Body),
                    read.Parameters);

        // c => build(back(c)): the value read back from the provider's form, then built into the object.
        private static Expression<Func<TValue, TValueObject>> Back(Expression<Func<TValue, TValueObject>> build, ValueConverter? stored) =>
            stored is null
                ? build
                : Expression.Lambda<Func<TValue, TValueObject>>(
                    ReplacingExpressionVisitor.Replace(build.Parameters[0], stored.ConvertFromProviderExpression.Body, build.Body),
                    stored.ConvertFromProviderExpression.Parameters);
    }
}
