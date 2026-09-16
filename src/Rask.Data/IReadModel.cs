using System.ComponentModel;

namespace Rask.Data;

/// <summary>
/// What a generated read face is: a primitive view of an entity's columns, with no behaviour and nothing to save.
/// </summary>
/// <remarks>
/// <para>
/// Not a type to implement. Rask generates one per mapped entity — <c>OrderRead</c> for <c>Order</c> — and it
/// lives in the read context, which holds no aggregates. That separation is the whole design: an aggregate
/// sees other aggregates only by id, so a write cannot cross a boundary by accident, while a read face carries
/// the navigations the write model is not allowed to have and can join across as many aggregates as it likes.
/// </para>
/// <para>
/// It exists because <c>ModelQuery&lt;T&gt;</c> has to tell a read face from an aggregate: the two open
/// different contexts.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IReadModel;
