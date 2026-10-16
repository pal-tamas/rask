using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     The traits an entity's base names, as the generator read them — the registry's form of
///     <c>Aggregate&lt;Guid, Timestamps, Versioned, …&gt;</c>. Written by generated code only.
/// </summary>
[Flags]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum TraitSet
{
    /// <summary>Nothing: no framework column, no form model, no generated delete.</summary>
    None = 0,

    /// <summary><see cref="Rask.Data.Created" />.</summary>
    Created = 1,

    /// <summary><see cref="Rask.Data.Updated" />.</summary>
    Updated = 2,

    /// <summary><see cref="Rask.Data.Versioned" />.</summary>
    Versioned = 4,

    /// <summary><see cref="Rask.Data.CreateModel" />, or half of <see cref="Rask.Data.Model" />.</summary>
    CreateModel = 8,

    /// <summary><see cref="Rask.Data.UpdateModel" />, or half of <see cref="Rask.Data.Model" />.</summary>
    UpdateModel = 16,

    /// <summary><see cref="Rask.Data.Deletable" />.</summary>
    Deletable = 32,

    /// <summary><see cref="Rask.Data.SoftDelete" />.</summary>
    SoftDelete = 64,

    /// <summary><see cref="Rask.Data.PerTenant" />, named on the root or reached through it.</summary>
    PerTenant = 128,

    /// <summary><see cref="Rask.Data.Broadcast" />.</summary>
    Broadcast = 256,
}
