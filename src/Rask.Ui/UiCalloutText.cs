namespace Rask;

/// <summary>
///     Flux's <c>flux:callout.text</c>: what a <see cref="UiCallout" /> says under its heading. It IS the
///     <c>&lt;div&gt;</c>, its children are the words, and its colour is its callout's.
/// </summary>
public sealed partial class UiCalloutText : UiElement
{
    private static readonly UiPartMarker Slot = new("slot", "text");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("text-sm", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Slot.With(Data);
}
