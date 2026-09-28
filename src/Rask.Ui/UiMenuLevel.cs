namespace Rask;

/// <summary>Where in a dropdown an item is rendering: the dropdown's scope, and the submenu it sits in.</summary>
/// <remarks>
///     Provided again by each <see cref="UiMenuSub" /> for its own children, so an item needs no idea how deep it is:
///     the nearest provider answers.
/// </remarks>
internal sealed record UiMenuLevel(UiMenuScope Scope, int Parent);
