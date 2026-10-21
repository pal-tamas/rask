using System.Collections.Concurrent;
using System.Linq.Expressions;
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
        return new Own<TValueObject, TValue>(value, build);
    }

    /// <summary>
    ///     How a face holding the value reads a column the write side stores through <paramref name="written" />:
    ///     nothing for Rask's own converter, whose column already holds the value, else the value rebuilt into the
    ///     value object and stored the app's way.
    /// </summary>
    public static ValueConverter? ForValue(ValueConverter written)
    {
        if (written is IOwn)
        {
            return null;
        }

        var valueObject = Nullable.GetUnderlyingType(written.ModelClrType) ?? written.ModelClrType;
        return FromValue.TryGetValue(valueObject, out var fromValue) ? fromValue.ComposeWith(written) : null;
    }

    private interface IOwn;

    private sealed class Own<TValueObject, TValue>(
        Expression<Func<TValueObject, TValue>> value, Expression<Func<TValue, TValueObject>> build)
        : ValueConverter<TValueObject, TValue>(value, build), IOwn;
}
