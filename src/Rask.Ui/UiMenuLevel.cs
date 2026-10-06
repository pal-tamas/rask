namespace Rask;

/// <summary>Where in a menu a row is rendering: the menu's scope, and the submenu it sits in.</summary>
/// <remarks>
///     Provided again by each <see cref="UiMenuSubmenu" /> for its own children, so a row needs no idea how deep it is:
///     the nearest provider answers.
/// </remarks>
internal sealed record UiMenuLevel(UiMenuScope Scope, int Parent);
