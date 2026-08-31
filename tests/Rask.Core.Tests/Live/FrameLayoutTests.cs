using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Every render allocates an array of these, one per element, attribute, text node and component in
// the tree. So the struct's SIZE is a render-hotpath fact, not an implementation detail: a field that
// pushes it over an alignment boundary costs bytes on every frame of every render, and shows up as an
// allocation regression nobody can attribute to anything.
//
// Pinned rather than described. RenderFrame.Opaque carries a comment claiming it "packs into the
// padding beside SelfClosing, so the frame does not grow" — which is exactly the sort of claim that
// is true when written and quietly false two fields later. This is what makes it checkable.
//
// A change here is not automatically wrong. It is something to have decided on purpose, with the
// benchmark's Allocated delta quoted — which is what updating these numbers should mean.
public class FrameLayoutTests
{
    [Fact]
    public void A_render_frame_still_fits_forty_bytes()
    {
        // On 64-bit: three references at 8 = 24, three ints (SubtreeLength, HtmlStart, HtmlEnd) at
        // 4 = 12, and then Kind + SelfClosing + Opaque in 3 bytes — because RenderFrameKind is
        // declared `: byte`. That 15 pads to 16, giving 40.
        //
        // The byte-backed enum is what buys the room. Widening it to a default int enum would push
        // the tail to 4 + 12 + 2 = 18, pad to 24, and cost 8 bytes on EVERY frame of every render.
        Assert.Equal(40, Unsafe.SizeOf<RenderFrame>());
    }

    [Fact]
    public void The_two_bools_cost_nothing_because_they_share_the_enums_word()
    {
        // The claim on RenderFrame.Opaque, made checkable: it says the field "packs into the padding
        // beside SelfClosing, so the frame does not grow", and it is right — a frame with neither bool
        // would still be 40, because 24 + (1 + 12) padded to 16 is the same 40.
        //
        // This existed because a benchmark showed RenderOnce growing 85.24 -> 87.12 KB and the field
        // was the obvious suspect. It is not the cause: the struct is the same size either way. Left
        // in place so the next person to suspect it can read the answer instead of re-measuring.
        Assert.Equal(0, Unsafe.SizeOf<RenderFrame>() % 8);
        Assert.Equal(40, Unsafe.SizeOf<RenderFrame>());
    }

    [Fact]
    public void A_lean_frame_is_no_larger_than_the_frame_it_snapshots()
    {
        // LeanFrame is the retained clean-subtree cache's copy, held for the lifetime of a cached
        // subtree rather than for one render. It carries strictly less than RenderFrame — no HtmlStart
        // or HtmlEnd — so it must never be the bigger of the two.
        Assert.True(
            Unsafe.SizeOf<LeanFrame>() <= Unsafe.SizeOf<RenderFrame>(),
            $"LeanFrame ({Unsafe.SizeOf<LeanFrame>()}) is larger than RenderFrame "
            + $"({Unsafe.SizeOf<RenderFrame>()}), which inverts what the cache is for.");
    }
}
