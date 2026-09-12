namespace Rask.Ui;

/// <summary>
/// The row of headline numbers at the top of a page.
/// </summary>
/// <remarks>
/// <para>
/// Two columns on a phone, four from <c>sm</c> up — so the reference's four-across row survives contact
/// with a 360px screen instead of squeezing four numbers into 80px each.
/// </para>
/// <para>
/// The hairlines are a <c>gap-px</c> over a lined background rather than borders on the cells. Cell borders
/// have to know how many siblings they have and which edge is last, and get it wrong the moment the number
/// of registered batteries changes; a lined background is correct for any count at any breakpoint.
/// </para>
/// </remarks>
public sealed partial class UiMetricRow : Component
{
    /// <summary>
    /// How many across from <c>sm</c> up — two, three, four or five. Four unless said otherwise; two on a phone
    /// regardless.
    /// </summary>
    /// <remarks>
    /// Spelled as whole literal class strings rather than composed from the number. Tailwind scans this file
    /// for class names as TEXT — an interpolated <c>sm:grid-cols-{n}</c> is not a class name it can see, and
    /// the utility would simply never be emitted, with an unstyled grid as the only symptom.
    /// </remarks>
    public int? Columns { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(
            "grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-base-300 bg-base-300 " + (Columns switch
            {
                // Two across on a phone already, so two across from sm up is nothing more to say.
                2 => "",
                // An odd number of tiles in two columns leaves the last slot empty, and because the
                // hairlines are the container's own background showing through the gaps, an empty slot is
                // not blank — it is a grey block that reads as a broken tile. The last one spans the row
                // instead, and goes back to a single column once there are enough columns to divide evenly.
                3 => "sm:grid-cols-3 [&>*:last-child]:col-span-2 sm:[&>*:last-child]:col-span-1",
                5 => "sm:grid-cols-5 [&>*:last-child]:col-span-2 sm:[&>*:last-child]:col-span-1",
                _ => "sm:grid-cols-4",
            }))[
            Children ?? []
        ];
}
