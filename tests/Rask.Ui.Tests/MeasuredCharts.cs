using System.Globalization;
using Rask.Core;
using Rask.Testing;

namespace Rask.UiTests;

/// <summary>
///     Charts as a page shows them once the browser has measured them.
/// </summary>
/// <remarks>
///     A chart draws nothing until it is told its box, and only a browser knows the box. So a test stands in
///     for the browser: it renders the charts live and writes each drawing's size into the field the runtime's
///     measuring hook writes it into (<c>data-rask-measure</c>), in the order the drawings are in the page.
/// </remarks>
internal static class MeasuredCharts
{
    /// <summary>The field a drawing's measured box comes back in.</summary>
    internal const string Field = "[data-rask-measure] input";

    /// <summary>The HTML of <paramref name="tree" /> with each of its drawings drawn for its box in <paramref name="boxes" />.</summary>
    internal static string Html(Component tree, params (double Width, double Height)[] boxes) =>
        Page(tree, boxes).Html;

    /// <summary>A live page of <paramref name="tree" /> with each of its drawings drawn for its box in <paramref name="boxes" />.</summary>
    internal static Page Page(Component tree, params (double Width, double Height)[] boxes)
    {
        var page = Testing.Page.Render(() => tree);
        var fields = page.FindAll(Field).Select(field => field.Attribute("data-rask-on-input")!).ToArray();
        Assert.Equal(boxes.Length, fields.Length);
        for (var i = 0; i < fields.Length; i++)
        {
            var size = string.Create(CultureInfo.InvariantCulture, $"{boxes[i].Width:R} {boxes[i].Height:R}");
            page.Invoke(fields[i], $$"""{"value":"{{size}}"}""").GetAwaiter().GetResult();
        }

        return page;
    }
}
