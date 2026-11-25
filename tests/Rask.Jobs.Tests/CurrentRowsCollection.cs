namespace Rask.Background.Tests;

/// <summary>The classes that stand in the process's current tenant and user rows, which run alone.</summary>
/// <remarks>
///     <c>CurrentRows.Use</c> keeps the rows in statics, and every processor reads its jobs' tenant and user rows
///     through them — so while one of these classes has its rows in place, a job any other class runs would be read
///     ahead from them, and its counts would be off. Parallelization is disabled outright, because serialising these
///     classes against each other is not enough: it is every running processor they must not meet.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentRowsCollection
{
    public const string Name = "current-rows";
}
