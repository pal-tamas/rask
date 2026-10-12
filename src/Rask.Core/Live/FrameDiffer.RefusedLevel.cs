namespace Rask.Core.Live;

// A level where one child took the PLACE of another: `cond ? A : B`, words that become an element, a
// placeholder that becomes a list, one page in the outlet giving way to the next. The positional walk answers
// with a remove and an insert the gate refuses, because it cannot tell which node stayed — and the reply used
// to be the whole page for it, menu and all.
//
// The page is not what changed. The level's PARENT is, so its children travel alone: the level's ops are
// rolled back and one trusted MorphSubtree of that parent takes their place, which the client reconciles
// with the same morph it runs over a whole page — the same pairing, the same guards — only no further than
// this one element. Deeper levels are settled first, so the outermost refused level speaks for those below it.
public static partial class FrameDiffer
{
    private static void MorphRefusedLevel(
        ReadOnlySpan<RenderFrame> newFrames, int newStart, int newEnd,
        List<EditOp> output,
        ReadOnlySpan<char> newHtml,
        DiffScratch scratch,
        int opCountAtEntry)
    {
        // No markup to cut from, or no element of the page's own to morph: the ops stand, the gate refuses
        // them, and the reply is the whole page as before.
        var (innerStart, innerEnd) = ChildrenHtmlRange(newFrames, newStart, newEnd, newHtml);
        if (innerStart < 0 || UnderDocumentShell(newFrames, newStart))
        {
            return;
        }

        scratch.ForgetMorphedFrom(opCountAtEntry);
        if (LiveOptions.IsDevelopment == true)
        {
            var refused = output.GetRange(opCountAtEntry, output.Count - opCountAtEntry);
            scratch.NoteMorphed(opCountAtEntry, innerEnd - innerStart, FullPageReply.ByPosition(refused, newHtml));
        }

        output.RemoveRange(opCountAtEntry, output.Count - opCountAtEntry);
        output.Add(new EditOp(EditOpKind.MorphSubtree, scratch.Path.ToArray(), null, null,
            trusted: true, htmlStart: innerStart, htmlEnd: innerEnd));
    }

    // The children of <body> and of <html> are the document's own: the runtime's script is among them, and so
    // is whatever the browser or a library put beside the page. A change of shape there is still a whole page.
    private static bool UnderDocumentShell(ReadOnlySpan<RenderFrame> frames, int firstChild)
    {
        var parent = firstChild - 1;
        while (parent >= 0 && frames[parent].Kind == RenderFrameKind.Attribute)
        {
            parent--;
        }

        return parent < 0 || frames[parent].Name is "body" or "html";
    }
}
