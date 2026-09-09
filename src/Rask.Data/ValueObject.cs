namespace Rask.Data;

/// <summary>
///     A value object: something a <see cref="Model" /> is described by rather than something the
///     database identifies. Mapped as an EF Core <b>complex type</b>, so its properties become columns on
///     the owning table.
/// </summary>
/// <remarks>
///     <para>
///         Declare one as a <c>record</c> (or <c>readonly record struct</c>) so it has value equality,
///         and mark it with this:
///     </para>
///     <example>
///         <code>
/// public sealed record Money(decimal Amount, string Currency) : IValueObject;
///
/// public sealed class Order : Model&lt;Guid&gt;
/// {
///     public Money Total { get; private set; } = new(0, "EUR");   // Total_Amount, Total_Currency
/// }
///         </code>
///     </example>
///     <para>
///         <b>A complex type, not an owned entity</b> — which is the distinction that matters. An owned
///         entity is a table row with hidden identity, so it can be null in ways a value has no business
///         being, is tracked separately, and quietly produces a join. A complex type is what its name
///         says: part of the row. That is what a value object is, so it is the default here, and it is
///         why <c>Money</c> can be shared between two entities without either owning it.
///     </para>
///     <para>
///         Nesting works — a value object made of value objects is mapped all the way down. What is not
///         automatic is a <em>collection</em> of them; configure that in the entity's own
///         <c>Configure</c>.
///     </para>
///     <para>
///         Marked by an interface rather than found by shape, because "a record with no identity" also
///         describes a great many types that are not meant to be columns, and a convention that mapped
///         them would be discovered as a surprising schema.
///     </para>
/// </remarks>
public interface IValueObject;
