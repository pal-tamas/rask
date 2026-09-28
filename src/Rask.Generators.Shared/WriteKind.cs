namespace Rask.Generators.Shared;

/// <summary>How generated code gets a value into a property it cannot assign directly.</summary>
internal enum WriteKind
{
    /// <summary>A public setter, assigned directly.</summary>
    Public,

    /// <summary>A non-public setter, reached through an <c>[UnsafeAccessor]</c> method.</summary>
    Setter,

    /// <summary>No usable setter but a compiler backing field, reached through an <c>[UnsafeAccessor]</c> field.</summary>
    Field,
}
