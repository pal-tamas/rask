namespace Rask.Data;

/// <summary>
/// A value object written as a plain class: two are equal when their <see cref="EqualityComponents" /> are,
/// in order, and they are of the same type. A <c>record</c> or a <c>record struct</c> needs no base — the language
/// already compares it by value.
/// </summary>
/// <example>
/// <code>
/// public sealed class Money : ValueObject
/// {
///     public decimal Amount { get; private set; }
///     public string Currency { get; private set; } = "";
///
///     protected override IEnumerable&lt;object?&gt; EqualityComponents()
///     {
///         yield return Amount;
///         yield return Currency;
///     }
/// }
/// </code>
/// </example>
#pragma warning disable S4035 // a base by design: Equals compares GetType(), so two value object types never equal
public abstract class ValueObject : IEquatable<ValueObject>
#pragma warning restore S4035
{
    /// <summary>Initializes the base.</summary>
    protected ValueObject()
    {
    }

    /// <summary>Equal to another of the same type whose <see cref="EqualityComponents" /> are equal, in order.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    /// <summary>Not equal: see <see cref="op_Equality" />.</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);

    /// <inheritdoc />
    public bool Equals(ValueObject? other) =>
        other is not null &&
        (ReferenceEquals(this, other) ||
         (other.GetType() == GetType() && EqualityComponents().SequenceEqual(other.EqualityComponents())));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GetType());

        foreach (var component in EqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>The values that make two of this type the same value, in a fixed order.</summary>
    protected abstract IEnumerable<object?> EqualityComponents();
}
