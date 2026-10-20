namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A component's page in the UI tab, as fluxui.dev lays one out: what it is, each example with its own code above
///     its result, and the reference of its steps.
/// </summary>
/// <remarks>The examples are the <see cref="UiDocsExample" />s given as children.</remarks>
internal sealed partial class UiComponentDoc : Component
{
    /// <summary>The page's entry in the catalog.</summary>
    public required UiDocsEntry Entry { get; set; }

    /// <summary>The sentence or two under the component's name.</summary>
    public required string Lead { get; set; }

    /// <summary>The parts and steps the reference lists.</summary>
    public required IReadOnlyList<UiApiPart> Api { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        UiDocsHeader.Entry(Entry).Lead(Lead),
        Div[Children ?? []],
        UiApiReference.Parts(Api)
    ];
}
