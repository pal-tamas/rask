namespace Rask.Generators.Shared;

/// <summary>How generated code reaches the collection it has to add to and remove from.</summary>
internal enum ModelChildAccess
{
    /// <summary>The property's own type is a writable collection, so the property is used directly.</summary>
    Property,

    /// <summary>The property hands out a read-only view, so its backing field is written instead.</summary>
    Field,

    /// <summary>
    ///     The property hands out a read-only collection it can be given anew — <c>Lines { get; private set; } = []</c> —
    ///     so a copy is synced and written back.
    /// </summary>
    Replace,

    /// <summary>None of these — Rask cannot sync this collection, and says so with RASK088.</summary>
    None,
}
