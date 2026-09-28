namespace Rask.Generators.Shared;

/// <summary>A collection of children on a read face, and the navigation back from each child to its root.</summary>
/// <param name="Name">The collection's name — <c>Lines</c>.</param>
/// <param name="ChildReadType">The child's read face — <c>global::Shop.OrderLineRead</c>.</param>
/// <param name="ChildWriteType">The child's write type — <c>global::Shop.OrderLine</c>.</param>
/// <param name="Inverse">The navigation back to the root on the child's face — <c>Order</c>.</param>
internal sealed record ReadChildCollection(
    string Name,
    string ChildReadType,
    string ChildWriteType,
    string Inverse);
