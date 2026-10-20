namespace Rask.Site.Features.UiDocs;

/// <summary>One part of a component — <c>Ui.Button</c>, <c>Ui.ButtonGroup</c> — and the steps it has of its own.</summary>
/// <param name="Entry">How a page reaches it: <c>Ui.Button</c>.</param>
/// <param name="Component">The type behind the entry, which <c>UiApiReferenceTests</c> holds the steps to.</param>
/// <param name="Flux">The Flux UI part it mirrors: <c>flux:button</c>.</param>
/// <param name="Summary">One line on what the part is.</param>
/// <param name="Steps">Its steps, in the order a reader meets them.</param>
internal sealed record UiApiPart(string Entry, Type Component, string Flux, string Summary, IReadOnlyList<UiApiStep> Steps);
