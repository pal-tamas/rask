using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>Everything one read face is mapped from.</summary>
/// <param name="readType">The generated read face — <c>OrderRead</c>.</param>
/// <param name="writeType">The entity it reads — <c>Order</c>.</param>
/// <param name="isRoot">Whether the entity is an aggregate root, which is what has a version and a soft delete.</param>
/// <param name="tableName">The table the convention says it lives in, overridden by the mirror.</param>
/// <param name="columns">Its columns.</param>
/// <param name="references">Its inferred navigations.</param>
/// <param name="children">Its child collections.</param>
/// <param name="valueCollections">Its collections of values.</param>
public sealed class ReadEntityMapping(
    [DynamicallyAccessedMembers(DataTrimming.Entity)] Type readType,
    [DynamicallyAccessedMembers(DataTrimming.Entity)] Type writeType,
    bool isRoot,
    string tableName,
    IReadOnlyList<ReadColumnMapping> columns,
    IReadOnlyList<ReadReferenceMapping> references,
    IReadOnlyList<ReadChildMapping> children,
    IReadOnlyList<ReadValueCollectionMapping> valueCollections)
{
    /// <summary>The generated read face.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    public Type ReadType { get; } = readType;

    /// <summary>The entity it reads.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    public Type WriteType { get; } = writeType;

    /// <summary>Whether the entity is an aggregate root.</summary>
    public bool IsRoot { get; } = isRoot;

    /// <summary>The table the convention derives.</summary>
    public string TableName { get; } = tableName;

    /// <summary>Its columns.</summary>
    public IReadOnlyList<ReadColumnMapping> Columns { get; } = columns;

    /// <summary>Its inferred navigations.</summary>
    public IReadOnlyList<ReadReferenceMapping> References { get; } = references;

    /// <summary>Its child collections.</summary>
    public IReadOnlyList<ReadChildMapping> Children { get; } = children;

    /// <summary>Its collections of values.</summary>
    public IReadOnlyList<ReadValueCollectionMapping> ValueCollections { get; } = valueCollections;
}
