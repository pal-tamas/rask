using System.Globalization;
using static Rask.Markup;

namespace Rask.Core.Virtualization;

/// <summary>
///     The two spacers a measured virtual list is drawn between (<c>ctx.Above</c>, <c>ctx.Below</c>): as tall as the
///     rows not drawn above and below the window, and marked for the runtime, which measures the rows between them
///     (rask-input.ts) and holds the box steady, follows the tail and scrolls to a row by them (rask-virtual.ts).
/// </summary>
/// <remarks>
///     The marks, on the spacer above: <c>data-rask-virtual="&lt;first row&gt; &lt;rows drawn&gt;"</c>,
///     <c>data-rask-virtual-anchor="&lt;VirtualAnchor&gt;"</c> where it is not <c>Start</c>, and
///     <c>data-rask-virtual-to="&lt;seq&gt; &lt;row&gt;"</c> while a scroll to a row waits for the browser. The one below says
///     <c>data-rask-virtual-below</c>. Each spacer is keyed, so a list of keyed rows stays on the keyed path, and says
///     <c>overflow-anchor: none</c>: the browser holds a row it shows, never a spacer.
/// </remarks>
internal static class VirtualSpacers
{
    internal const string AboveKey = "rask:virtual-above";

    internal const string BelowKey = "rask:virtual-below";

    /// <summary>The spacer above the window.</summary>
    /// <param name="state">The window.</param>
    /// <param name="row">A <c>&lt;tr&gt;</c>, for rows inside a <c>&lt;tbody&gt;</c>; else a <c>&lt;div&gt;</c>.</param>
    /// <returns>The spacer.</returns>
    internal static Component Above(VirtualizationState state, bool row)
    {
        var marks = state.Marks;
        var data = new List<(string, string?)>(3)
        {
            ("rask-virtual", Number(state.StartIndex) + " " + Number(state.VisibleValues.Count)),
        };
        if (marks.Anchor != VirtualAnchor.Start)
        {
            data.Add(("rask-virtual-anchor", Number((int)marks.Anchor)));
        }

        if (marks.AimSeq > 0)
        {
            data.Add(("rask-virtual-to", Number(marks.AimSeq) + " " + Number(marks.AimIndex)));
        }

        var spacer = Spacer(AboveKey, state.OffsetBefore, row, [.. data]);
        spacer.Ref = state.AboveRef;
        return spacer;
    }

    /// <summary>The spacer below the window.</summary>
    /// <param name="state">The window.</param>
    /// <param name="row">A <c>&lt;tr&gt;</c>, for rows inside a <c>&lt;tbody&gt;</c>; else a <c>&lt;div&gt;</c>.</param>
    /// <returns>The spacer.</returns>
    internal static Component Below(VirtualizationState state, bool row) =>
        Spacer(BelowKey, state.OffsetAfter, row, [("rask-virtual-below", "")]);

    private static Element Spacer(string key, int height, bool row, (string, string?)[] data)
    {
        var style = "height:" + Number(height) + "px;flex-shrink:0;overflow-anchor:none";
        return row
            ? Tr.Key(key).Style(style).Aria("hidden", "true").Data(data)
            : Div.Key(key).Style(style).Aria("hidden", "true").Data(data);
    }

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
