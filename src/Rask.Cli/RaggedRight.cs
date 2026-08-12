using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rask.Cli;

/// <summary>
/// Renders <paramref name="inner"/> with the trailing whitespace stripped from every line.
/// </summary>
/// <remarks>
/// A grid or table pads each cell out to its column width, which means every row shorter than the widest
/// one ends in spaces. On screen that is invisible; in a file it is not. <c>rask doctor > report.txt</c>,
/// a CI log, and a captured test output all carry that padding, and this repo's own <c>.editorconfig</c>
/// says trailing whitespace is a defect. The hand-rolled columns this replaced never produced any,
/// because they padded the label and put the free-form text last — so stripping it here is what keeps the
/// output the same as it always was, only now wrapped and aligned by something that understands width.
/// </remarks>
internal sealed class RaggedRight(IRenderable inner) : IRenderable
{
    public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        // Segments arrive in reading order with explicit line-break segments between rows, so a line is
        // buffered whole: only once it ends is it known which of its whitespace was padding rather than
        // the gap between two columns.
        var line = new List<Segment>();

        foreach (var segment in inner.Render(options, maxWidth))
        {
            if (!segment.IsLineBreak)
            {
                line.Add(segment);
                continue;
            }

            foreach (var trimmed in TrimEnd(line))
            {
                yield return trimmed;
            }

            line.Clear();
            yield return segment;
        }

        foreach (var trimmed in TrimEnd(line))
        {
            yield return trimmed;
        }
    }

    /// <summary>One line's segments, with the padding at its end removed.</summary>
    private static IEnumerable<Segment> TrimEnd(List<Segment> line)
    {
        var last = line.Count - 1;
        while (last >= 0 && string.IsNullOrWhiteSpace(line[last].Text))
        {
            last--;
        }

        for (var i = 0; i <= last; i++)
        {
            // The final segment may itself end in padding — a cell whose text and padding were emitted
            // together — so it is trimmed rather than kept whole.
            yield return i == last
                ? new Segment(line[i].Text.TrimEnd(), line[i].Style)
                : line[i];
        }
    }
}
