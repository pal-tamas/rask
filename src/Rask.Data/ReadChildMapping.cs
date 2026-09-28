using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>A collection of children on a read face, and the navigation back from each child.</summary>
/// <param name="collection">The collection on the root's read face — <c>Lines</c>.</param>
/// <param name="childReadType">The child's read face — <c>OrderLineRead</c>.</param>
/// <param name="childWriteType">The child's write type, for finding the relationship EF built.</param>
/// <param name="inverse">The navigation back to the root on the child's read face — <c>Order</c>.</param>
public sealed class ReadChildMapping(
    string collection,
    [DynamicallyAccessedMembers(DataTrimming.Entity)] Type childReadType,
    [DynamicallyAccessedMembers(DataTrimming.Entity)] Type childWriteType,
    string inverse)
{
    /// <summary>The collection on the root's read face.</summary>
    public string Collection { get; } = collection;

    /// <summary>The child's read face.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    public Type ChildReadType { get; } = childReadType;

    /// <summary>The child's write type.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    public Type ChildWriteType { get; } = childWriteType;

    /// <summary>The navigation back to the root on the child's read face.</summary>
    public string Inverse { get; } = inverse;
}
