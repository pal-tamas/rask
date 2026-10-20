namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A component's page in the UI tab, as fluxui.dev lays one out: what it is, every example with its code above it,
///     and the reference of its steps.
/// </summary>
internal sealed partial class UiComponentDoc : Component
{
    /// <summary>The page's entry in the catalog.</summary>
    public required UiDocsEntry Entry { get; set; }

    /// <summary>The sentence or two under the component's name.</summary>
    public required string Lead { get; set; }

    /// <summary>The demo's source files, shown above it: <c>["UiKitButtonDemo.cs"]</c>.</summary>
    public required IReadOnlyList<string> Files { get; set; }

    /// <summary>The parts and steps the reference lists.</summary>
    public required IReadOnlyList<UiApiPart> Api { get; set; }

    /// <summary>The live example: the demo component whose source <see cref="Files" /> names.</summary>
    public required Component Demo { get; set; }

    /// <summary>What the code pane says about the example, when there is something worth saying.</summary>
    public string? Notes { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        UiDocsHeader.Entry(Entry).Lead(Lead),
        CodeSample.Files(Files).Notes(Notes).Result(Demo),
        UiApiReference.Parts(Api)
    ];
}
