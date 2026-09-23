using System.Globalization;

namespace Rask.Cqrs;

/// <summary>
///     What the generator knows about a notification marked <see cref="ForAttribute{TScope}" />: how to read its key,
///     who may watch it, and how its key crosses the wire. Public only so generated code can build one; you do not use
///     it directly.
/// </summary>
public sealed class NotificationScope
{
    /// <summary>Describes one scoped notification type.</summary>
    /// <param name="scope">What the key identifies — the <c>TScope</c> of <see cref="ForAttribute{TScope}" />.</param>
    /// <param name="keyType">The type of the marked property.</param>
    /// <param name="keyOf">Reads the key off a notification instance.</param>
    /// <param name="canWatch">Asks the <see cref="IWatchPolicy{TScope}" /> in the subscriber's scope.</param>
    /// <param name="parseKey">
    ///     Rebuilds a key from its invariant string, or null when the key type has no text form — such a notification
    ///     is watched in-process only.
    /// </param>
    public NotificationScope(
        Type scope,
        Type keyType,
        Func<object, object?> keyOf,
        Func<IServiceProvider, object, CancellationToken, Task<bool>> canWatch,
        Func<string, object>? parseKey)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(canWatch);
        Scope = scope;
        KeyType = keyType;
        KeyOf = keyOf;
        CanWatch = canWatch;
        ParseKey = parseKey;
    }

    /// <summary>What the key identifies.</summary>
    public Type Scope { get; }

    /// <summary>The type of the marked property.</summary>
    public Type KeyType { get; }

    /// <summary>Reads the key off a notification instance.</summary>
    public Func<object, object?> KeyOf { get; }

    /// <summary>Asks the policy for <see cref="Scope" /> whether the subscriber in the given scope may watch a key.</summary>
    public Func<IServiceProvider, object, CancellationToken, Task<bool>> CanWatch { get; }

    /// <summary>Rebuilds a key from its invariant string; null when the key has no text form.</summary>
    public Func<string, object>? ParseKey { get; }

    /// <summary>The key as it travels in a URL: invariant, so a server in another culture parses what a client wrote.</summary>
    internal static string FormatKey(object key) =>
        key is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : key.ToString() ?? string.Empty;
}
