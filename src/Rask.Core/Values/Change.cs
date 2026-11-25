namespace Rask.Values;

/// <summary>
///     What an operation does to a value it may leave alone: keep it, clear it, or set it —
///     <c>public void Change(Change&lt;Password&gt; password) =&gt; Password = password.Applied(Password);</c>
/// </summary>
/// <remarks>
///     <para>
///         The headline case is a password: a form over the operation (<c>Form.For</c>) shows its field empty, and an
///         empty field keeps the stored one. A value typed in is <see cref="Set" />, checked through its value object's
///         rules. A parameter over a value that may be absent — <c>Change&lt;Nickname?&gt;</c> — also gets a "Clear" control
///         beside its field, and ticked it is <see cref="Clear" />.
///     </para>
///     <para>
///         The default is <see cref="Keep" />, so an operation called from code without it changes nothing.
///     </para>
/// </remarks>
/// <typeparam name="T">The value: a value object, or a plain value.</typeparam>
#pragma warning disable CA1000 // the change's own states, as ImmutableArray<T>.Empty is: Change<Password>.Keep reads as the domain says it
public readonly record struct Change<T>
{
    private Change(ChangeKind kind, T? value) => (Kind, Value) = (kind, value);

    /// <summary>Leave the value as it is.</summary>
    public static Change<T> Keep => default;

    /// <summary>Take the value away: what <see cref="Applied" /> hands back is the default — <see langword="null" />.</summary>
    public static Change<T> Clear => new(ChangeKind.Clear, default);

    /// <summary>What it does: <see cref="ChangeKind.Keep" />, <see cref="ChangeKind.Clear" /> or <see cref="ChangeKind.Set" />.</summary>
    public ChangeKind Kind { get; }

    /// <summary>
    ///     The value it sets, for <see cref="ChangeKind.Set" />; for one made by <see cref="Kept" />, the value it keeps, as a
    ///     form shows it; the default otherwise.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    ///     Leave the value as it is, while a form over a model of its own shows it:
    ///     <c>Start = Change&lt;Opening?&gt;.Kept(part.Start)</c>. Its <see cref="Kind" /> is <see cref="ChangeKind.Keep" />,
    ///     so <see cref="Applied" /> hands back the value as it stands, whatever was shown. Fields bound to its members
    ///     (<c>() =&gt; m.Start.Value!.Date</c>) leave it so until a new value is typed in, which they make a
    ///     <see cref="Set" />, and make it <see cref="Clear" /> once they are all emptied.
    /// </summary>
    /// <param name="value">The value as it stands, which the form shows.</param>
    /// <returns>The change that keeps it.</returns>
    public static Change<T> Kept(T? value) => new(ChangeKind.Keep, value);

    /// <summary>Set the value to <paramref name="value" />.</summary>
    /// <param name="value">The new value.</param>
    /// <returns>The change that sets it.</returns>
    public static Change<T> Set(T value) => new(ChangeKind.Set, value);

    /// <summary>
    ///     The value once the change is made to <paramref name="current" />: <paramref name="current" /> kept, the default
    ///     for a clear, the new value for a set.
    /// </summary>
    /// <param name="current">The value as it stands.</param>
    /// <returns>The value as it is to stand.</returns>
    public T? Applied(T? current) => Kind switch
    {
        ChangeKind.Clear => default,
        ChangeKind.Set => Value,
        _ => current,
    };
}
#pragma warning restore CA1000
