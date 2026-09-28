namespace Rask.Generators.Shared;

/// <summary>The part a property of an entity plays in its generated model.</summary>
/// <remarks>
/// There is no key role: the model never carries <c>Entity&lt;TId&gt;.Id</c> (see <see cref="ModelShape.Key" />).
/// </remarks>
internal enum ModelMemberRole
{
    /// <summary>An ordinary value, copied both ways.</summary>
    Value,

    /// <summary>An aggregate's <c>int Version</c>: read into the model, never written back.</summary>
    Version,
}
