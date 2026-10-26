namespace Rask.UiTests;

/// <summary>
///     The classes whose pages read and write <c>FilteredOrders</c>' process-wide fields (its escape-hatch tag, its
///     open count), run as one xUnit collection so that no two of them do it at once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class FilteredOrdersCollection
{
    public const string Name = "filtered-orders";
}
