namespace Rask.Generators.Shared;

/// <summary>How generated code gets a value back into an entity property.</summary>
internal enum ModelWriteKind
{
    /// <summary>A public setter, assigned directly.</summary>
    Public,

    /// <summary>A non-public setter, reached through an <c>[UnsafeAccessor]</c> method.</summary>
    Setter,

    /// <summary>No usable setter but a compiler backing field, reached through an <c>[UnsafeAccessor]</c> field.</summary>
    Field,
}
