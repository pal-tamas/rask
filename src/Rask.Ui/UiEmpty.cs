namespace Rask.Ui;

/// <summary>What a panel shows when there is nothing in it — and, where it helps, why.</summary>
/// <remarks>
/// <para>
/// No border of its own, so it reads the same wherever it lands: inside a <see cref="UiCard" />, as a
/// <see cref="UiDataGrid{T,TKey}.Empty" /> in the table's own empty row, or on its own on a page.
/// </para>
/// <para>
/// A heading and then the detail, because the first line is the answer ("No queue called jobs") and the second
/// is the reason. A reader who stops after one line should already have the answer.
/// </para>
/// </remarks>
public sealed partial class UiEmpty : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public required string Heading { get; set; }

    public string? Detail { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("py-6 text-center")[
            Div.Class("text-base font-medium text-base-content")[Heading],
            Detail is null ? null : Div.Class("mx-auto mt-1 max-w-prose text-sm opacity-60")[Detail]
        ];
}
