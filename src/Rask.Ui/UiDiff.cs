namespace Rask.Ui;

/// <summary>
/// Two versions of something, with a handle to wipe between them.
/// </summary>
/// <remarks>
/// The handle is a <c>tabindex</c>-bearing div that daisyUI drives from focus and pointer position in
/// CSS, so the comparison works with no script. It is a visual comparison and nothing more: give both
/// sides real alternative text, because the difference itself is not announced.
/// </remarks>
public sealed partial class UiDiff : Component
{
    public required Component Before { get; set; }

    public required Component After { get; set; }

    /// <summary>The accessible name for the handle.</summary>
    public string? HandleLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Figure.Class(UiClass.Compose("diff aspect-16/9", Class))[
            Div.Class("diff-item-1")[Before],
            Div.Class("diff-item-2")[After],
            Div
                .Class("diff-resizer")
                .Attributes(("tabindex", "0"))
                .Aria(new Dictionary<string, string?> { ["label"] = HandleLabel ?? "Compare" })
        ];
}
