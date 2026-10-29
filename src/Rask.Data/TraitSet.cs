using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     The traits an entity implements, as the generator read them — the registry's form of
///     <c>Aggregate&lt;Guid&gt;, ITimestamps, IVersioned, …</c>. Written by generated code only.
/// </summary>
[Flags]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum TraitSet
{
    /// <summary>Nothing: no framework column, no form model, no generated delete.</summary>
    None = 0,

    /// <summary><see cref="ICreated" />.</summary>
    Created = 1,

    /// <summary><see cref="IUpdated" />.</summary>
    Updated = 2,

    /// <summary><see cref="IVersioned" />.</summary>
    Versioned = 4,

    /// <summary><see cref="ICreateFormModel" />, or half of <see cref="IFormModel" />.</summary>
    CreateModel = 8,

    /// <summary><see cref="IUpdateFormModel" />, or half of <see cref="IFormModel" />.</summary>
    UpdateModel = 16,


    /// <summary><see cref="ISoftDeletable" />.</summary>
    SoftDelete = 64,

    /// <summary><see cref="IPerTenant" />, named on the root or reached through it.</summary>
    PerTenant = 128,

    /// <summary><see cref="IBroadcast" />.</summary>
    Broadcast = 256,
}
