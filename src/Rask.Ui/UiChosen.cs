namespace Rask;

/// <summary>
///     What a <c>.Choices(…)</c> made: what it was handed, the options drawn now, the search that answers with rows,
///     and whether choosing nothing is an answer.
/// </summary>
internal sealed record UiChosen(object Said, Component?[] Rows, Fn<string, Task<IEnumerable<Component?>>>? Find, bool MayBeEmpty);
