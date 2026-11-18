namespace Rask.Data;

/// <summary>
///     The current tenant's or user's key, between the <see cref="Guid" /> the framework carries it in and the type
///     the app's own aggregate is keyed by.
/// </summary>
/// <remarks>
///     A <see cref="Guid" /> is itself, a <see cref="string" /> is that <see cref="Guid" /> written out (<c>D</c>
///     format), and an <see cref="int" /> or a <see cref="long" /> is the number the <see cref="Guid" /> carries
///     (<see cref="TenantNumber" />). The build allows no other key, so the last arm is never reached.
/// </remarks>
internal static class CurrentKey
{
    /// <summary>Whether <typeparamref name="TKey" /> is a number key, carried inside the <see cref="Guid" />.</summary>
    internal static bool IsNumber<TKey>() => typeof(TKey) == typeof(int) || typeof(TKey) == typeof(long);

    /// <summary><paramref name="key" /> as the <typeparamref name="TKey" /> <paramref name="keyedBy" /> is keyed by.</summary>
    /// <param name="key">The key as the framework carries it.</param>
    /// <param name="kind">Whose key it is, for the sentence a key that is not one throws.</param>
    /// <param name="keyedBy">The aggregate keyed by <typeparamref name="TKey" />, as the sentence names it.</param>
    /// <exception cref="InvalidOperationException">
    ///     <typeparamref name="TKey" /> is a number, and <paramref name="key" /> carries none that fits it.
    /// </exception>
    internal static TKey Of<TKey>(Guid key, CurrentRowKind kind, string keyedBy)
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return (TKey)(object)key;
        }

        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)key.ToString("D");
        }

        if (typeof(TKey) == typeof(long))
        {
            return (TKey)(object)Number<TKey>(key, kind, keyedBy);
        }

        if (typeof(TKey) == typeof(int))
        {
            var number = Number<TKey>(key, kind, keyedBy);
            return number is >= int.MinValue and <= int.MaxValue
                ? (TKey)(object)(int)number
                : throw NotTheKey<TKey>(key, kind, keyedBy);
        }

        throw new NotSupportedException(
            $"{keyedBy} is keyed by {typeof(TKey).Name}; the current {Name(kind)} is a Guid, a string, an int or a long.");
    }

    /// <summary>The <see cref="Guid" /> the framework carries <paramref name="key" /> in.</summary>
    /// <param name="key">An <see cref="int" />, a <see cref="long" /> or a <see cref="Guid" />.</param>
    internal static Guid ToGuid<TKey>(TKey key)
        where TKey : struct
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return (Guid)(object)key;
        }

        if (typeof(TKey) == typeof(int))
        {
            return TenantNumber.ToGuid((int)(object)key);
        }

        return typeof(TKey) == typeof(long)
            ? TenantNumber.ToGuid((long)(object)key)
            : throw new NotSupportedException($"A {typeof(TKey).Name} key is not one the framework carries; it carries a Guid, an int or a long.");
    }

    private static long Number<TKey>(Guid key, CurrentRowKind kind, string keyedBy) =>
        TenantNumber.TryRead(key, out var number) ? number : throw NotTheKey<TKey>(key, kind, keyedBy);

    private static InvalidOperationException NotTheKey<TKey>(Guid key, CurrentRowKind kind, string keyedBy) =>
        new($"{keyedBy} is keyed by {typeof(TKey).Name}, and the current {Name(kind)} {key} is not one: it is an " +
            "identifier in its own right, not a number carried as one. Open the work with the number itself — " +
            (kind is CurrentRowKind.Tenant ? "Tenant.Use(42)." : "Current.UseUser(42)."));

    private static string Name(CurrentRowKind kind) => kind is CurrentRowKind.Tenant ? "tenant" : "user";
}
