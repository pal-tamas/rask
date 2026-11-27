using System.ComponentModel;
using System.Globalization;

namespace Rask.Data;

/// <summary>
///     A reference to another aggregate, across the border: the row stores only its id, and the aggregate it names is
///     never loaded, saved or tracked as part of this one.
/// </summary>
/// <remarks>
///     <code>
/// public sealed partial class Fueling : Aggregate&lt;FuelingId&gt;
/// {
///     public partial BelongsTo&lt;Vehicle&gt;? Vehicle { get; }      // Rask generates VehicleId? VehicleId { get; private set; }
///
///     public void Fill(VehicleId vehicle) =&gt; VehicleId = vehicle;   // assigned through the id, never an object
/// }
///
/// VehicleRead? vehicle = await fueling.Vehicle.Read();               // the vehicle's read face, by its id
/// var rows = await Fueling.Include(f =&gt; f.Vehicle).ThenInclude(v =&gt; v.Owner);
///     </code>
///     <para>
///         Rask generates the foreign key beside the declaration — <c>VehicleId</c>, of the vehicle's own key type, nullable
///         when the reference is (<c>BelongsTo&lt;Vehicle&gt;?</c>) and required when it is not — and maps it as a
///         relationship to the vehicle's table that never cascades: deleting a vehicle a fueling still names is refused.
///         The column is the one a hand-written <c>VehicleId</c> had, so moving to a <c>BelongsTo</c> writes no migration.
///     </para>
///     <para>
///         Nothing here reaches the other aggregate's behaviour. Reading it is the read face's: <c>Read()</c> (one row, or
///         a projection of it), the batched <c>Read()</c> of many references, or <c>Include</c> on a query, which is the
///         read face's navigation of the same name. Changing it is that aggregate's own write.
///     </para>
/// </remarks>
/// <typeparam name="TAggregate">The aggregate referred to.</typeparam>
public sealed class BelongsTo<TAggregate> : IEquatable<BelongsTo<TAggregate>>
    where TAggregate : class, IAggregate
{
    internal BelongsTo(object key) => Key = key;

    /// <summary>The referenced aggregate's id, boxed: what the generated <c>Id</c> unwraps. Read <c>Id</c> instead.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object Key { get; }

    /// <summary>Two references to the same row: the same aggregate type and the same id.</summary>
    /// <param name="left">One reference.</param>
    /// <param name="right">The other.</param>
    public static bool operator ==(BelongsTo<TAggregate>? left, BelongsTo<TAggregate>? right) => Equals(left, right);

    /// <summary>References to different rows: see <see cref="op_Equality" />.</summary>
    /// <param name="left">One reference.</param>
    /// <param name="right">The other.</param>
    public static bool operator !=(BelongsTo<TAggregate>? left, BelongsTo<TAggregate>? right) => !Equals(left, right);

    /// <summary>Whether <paramref name="other" /> names the same row.</summary>
    /// <param name="other">The other reference.</param>
    public bool Equals(BelongsTo<TAggregate>? other) => other is not null && Key.Equals(other.Key);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as BelongsTo<TAggregate>);

    /// <inheritdoc />
    public override int GetHashCode() => Key.GetHashCode();

    /// <summary>The id, as the id prints itself: <c>42</c>, <c>3e18ad13-…</c>.</summary>
    public override string ToString() => Convert.ToString(Key, CultureInfo.InvariantCulture) ?? string.Empty;
}
