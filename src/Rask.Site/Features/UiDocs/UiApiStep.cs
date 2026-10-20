namespace Rask.Site.Features.UiDocs;

/// <summary>One step of a component's chain, as its reference table lists it.</summary>
/// <param name="Name">The step: the property it sets, <c>Variant</c>.</param>
/// <param name="Takes">The value it takes. An enum's values are listed from the type itself.</param>
/// <param name="Description">What it does, and what it is when unset.</param>
/// <param name="TakesText">How to name what it takes, where the type alone says it badly.</param>
internal sealed record UiApiStep(string Name, Type Takes, string Description, string? TakesText = null);
