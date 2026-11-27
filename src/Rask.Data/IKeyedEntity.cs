namespace Rask.Data;

/// <summary>An <see cref="Entity{TId}" />'s key, for code that matches entities of any key type: <see cref="HasMany{TChild}" />.</summary>
internal interface IKeyedEntity
{
    /// <summary>The entity's <c>Id</c>, boxed; null before it has one (an integer key before its insert).</summary>
    object? Key { get; }
}
