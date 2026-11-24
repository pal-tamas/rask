using System.Diagnostics;

namespace Rask.Values;

/// <summary>
///     A value the app stores but nobody reads back: an API key, a token a partner issued —
///     <c>public Secret&lt;ApiKey&gt;? Key { get; private set; }</c>. Made with <see cref="Secret.Of{T}" />.
/// </summary>
/// <remarks>
///     <para>
///         Rask.Data stores it encrypted, through the host's ASP.NET Core Data Protection key ring, so the column — and a
///         JSON column that holds one inside a value object — holds ciphertext, and a database dump, a backup or a log
///         of the SQL parameters shows nothing usable.
///     </para>
///     <para>
///         It never shows what it holds: <see cref="ToString" /> and the debugger say <c>"••••"</c>, and it has no
///         public property, so a serializer writes it as <c>{}</c>. The read face leaves it out, and a form built over
///         the aggregate has no field for it — so it is never sent to a browser, and never prefilled into a form. An
///         operation takes the new value as the plain value object (a <c>Change&lt;ApiKey&gt;</c>, whose empty field
///         keeps the stored one) and wraps it with <see cref="Secret.Applied{T}" />. Read <see cref="Reveal" /> where
///         the value is used — the request to the partner — and nowhere else.
///     </para>
/// </remarks>
/// <typeparam name="T">What it holds: a <see cref="string" />, or a value object that holds one text.</typeparam>
[DebuggerDisplay("••••")]
[DebuggerTypeProxy(typeof(Secret<>.View))]
public sealed class Secret<T> : IEquatable<Secret<T>>
    where T : class
{
    /// <summary>What a secret shows in place of what it holds.</summary>
    internal const string Masked = "••••";

    private readonly T _value;

    internal Secret(T value) => _value = value;

    /// <summary>What it holds, for the one place that uses it.</summary>
    /// <returns>The value it was made from.</returns>
    public T Reveal() => _value;

    /// <summary>The mask, never what it holds — so a log line or an interpolation does not leak it.</summary>
    /// <returns><c>"••••"</c>.</returns>
    public override string ToString() => Masked;

    /// <summary>Whether <paramref name="other" /> holds the same value.</summary>
    /// <param name="other">The other secret.</param>
    /// <returns><see langword="true" /> when both hold equal values.</returns>
    public bool Equals(Secret<T>? other) => other is not null && EqualityComparer<T>.Default.Equals(_value, other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Secret<T>);

    /// <inheritdoc />
    public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(_value);

    // What the debugger expands a secret into: the mask, never the value.
    private sealed class View(Secret<T> secret)
    {
        public string Value { get; } = secret is null ? "" : Masked;
    }
}

/// <summary>Makes a <see cref="Secret{T}" />.</summary>
public static class Secret
{
    /// <summary>Keeps <paramref name="value" /> as a secret: <c>Key = Secret.Of(key);</c></summary>
    /// <typeparam name="T">What it holds.</typeparam>
    /// <param name="value">The value, already checked by its own rules.</param>
    /// <returns>The secret holding it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
    public static Secret<T> Of<T>(T value)
        where T : class =>
        new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>
    ///     The secret once <paramref name="change" /> is made to <paramref name="current" />:
    ///     <c>public void Connect(Change&lt;ApiKey&gt; key) =&gt; Key = Secret.Applied(key, Key);</c> — an empty field keeps
    ///     the stored one, a value typed in replaces it, a clear takes it away.
    /// </summary>
    /// <typeparam name="T">What it holds.</typeparam>
    /// <param name="change">What the form or the caller asked for.</param>
    /// <param name="current">The secret as it is stored.</param>
    /// <returns>The secret as it is to be stored, or null when it is cleared.</returns>
    public static Secret<T>? Applied<T>(Change<T> change, Secret<T>? current)
        where T : class => change.Kind switch
        {
            ChangeKind.Clear => null,
            ChangeKind.Set when change.Value is { } value => new Secret<T>(value),
            ChangeKind.Set => null,
            _ => current,
        };
}
