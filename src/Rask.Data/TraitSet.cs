using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     The traits an entity says, as the generator read them — the registry's form of
///     <c>Aggregate&lt;Guid, Timestamps, Versioned, …&gt;</c>. Written by generated code only.
/// </summary>
[Flags]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum TraitSet
{
    /// <summary>Nothing: no framework column, no form model, no generated delete.</summary>
    None = 0,

    /// <summary><see cref="Rask.Data.IHasCreatedAt" />.</summary>
    Created = 1,

    /// <summary><see cref="Rask.Data.IHasUpdatedAt" />.</summary>
    Updated = 2,

    /// <summary><see cref="Rask.Data.IVersioned" />.</summary>
    Versioned = 4,

    /// <summary><see cref="Rask.Data.IHasCreateForm" />, or half of <see cref="Rask.Data.IHasForm" />.</summary>
    CreateModel = 8,

    /// <summary><see cref="Rask.Data.IHasUpdateForm" />, or half of <see cref="Rask.Data.IHasForm" />.</summary>
    UpdateModel = 16,

    /// <summary><see cref="Rask.Data.IDeletable" />.</summary>
    Deletable = 32,

    /// <summary><see cref="Rask.Data.ISoftDeletes" />.</summary>
    SoftDelete = 64,

    /// <summary><see cref="Rask.Data.IBelongsTo{TTenant}" />, named on the root or reached through it.</summary>
    PerTenant = 128,

    /// <summary><see cref="Rask.Data.IBroadcasts" />.</summary>
    Broadcast = 256,

    /// <summary><see cref="Rask.Data.ISoftDeletes" />.</summary>
    SoftRemoved = 512,
}
