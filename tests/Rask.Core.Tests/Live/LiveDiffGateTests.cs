using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// LiveDiffGate holds the three diff-codec gating helpers previously copied into both
// Rask.Server.LiveSession and Rask.Wasm.WasmLiveSession. They were untestable as private
// statics — covered only indirectly via the e2e suite. Now that they live in one shared
// internal class, pin their contracts directly: a regression here would silently change
// head-fragment shipping or structural-op safety on BOTH transports at once.
public class LiveDiffGateTests
{
    private static EditOp Op(EditOpKind kind, bool trusted = false) =>
        new(kind, [0], null, null, 0, trusted);

    // --- HeadUnchanged -----------------------------------------------------

    [Fact]
    public void HeadUnchanged_IdenticalHead_DifferentBody_ReturnsTrue()
    {
        const string head = "<!DOCTYPE html><html><head><title>A</title></head>";
        var a = head + "<body><p>one</p></body></html>";
        var b = head + "<body><p>two</p></body></html>";

        Assert.True(LiveDiffGate.HeadUnchanged(a, b));
    }

    [Fact]
    public void HeadUnchanged_DifferentTitle_ReturnsFalse()
    {
        var a = "<html><head><title>A</title></head><body>x</body></html>";
        var b = "<html><head><title>B</title></head><body>x</body></html>";

        Assert.False(LiveDiffGate.HeadUnchanged(a, b));
    }

    [Fact]
    public void HeadUnchanged_DifferentHeadLength_ReturnsFalse()
    {
        // Same prefix up to a point but the </head> lands at a different offset.
        var a = "<html><head><title>A</title></head><body>x</body></html>";
        var b = "<html><head><title>A</title><meta charset=\"utf-8\"></head><body>x</body></html>";

        Assert.False(LiveDiffGate.HeadUnchanged(a, b));
    }

    [Fact]
    public void HeadUnchanged_MissingHeadCloseInEither_ReturnsFalse()
    {
        const string withHead = "<html><head><title>A</title></head><body>x</body></html>";
        const string noHead = "<html><body>x</body></html>";

        // Missing in the first arg, the second arg, or both → treat as changed, never
        // an unsafe "true" that would freeze a stale <head>.
        Assert.False(LiveDiffGate.HeadUnchanged(noHead, withHead));
        Assert.False(LiveDiffGate.HeadUnchanged(withHead, noHead));
        Assert.False(LiveDiffGate.HeadUnchanged(noHead, noHead));
    }

    // --- ExtractHead -------------------------------------------------------

    [Fact]
    public void ExtractHead_WellFormed_ReturnsHeadElement()
    {
        var html = "<!DOCTYPE html><html><head><title>A</title></head><body>x</body></html>";

        Assert.Equal("<head><title>A</title></head>", LiveDiffGate.ExtractHead(html));
    }

    [Fact]
    public void ExtractHead_HeadWithAttributes_IncludesOpenTag()
    {
        var html = "<html><head data-x=\"1\"><meta charset=\"utf-8\"></head><body>x</body></html>";

        Assert.Equal("<head data-x=\"1\"><meta charset=\"utf-8\"></head>", LiveDiffGate.ExtractHead(html));
    }

    [Fact]
    public void ExtractHead_NoHeadOpen_ReturnsNull() =>
        Assert.Null(LiveDiffGate.ExtractHead("<html><body>x</body></html>"));

    [Fact]
    public void ExtractHead_NoHeadClose_ReturnsNull() =>
        Assert.Null(LiveDiffGate.ExtractHead("<html><head><title>A</title><body>x</body></html>"));

    [Fact]
    public void ExtractHead_CloseBeforeOpen_ReturnsNull()
    {
        // Pathological ordering: </head> appears before any <head — close <= open → null.
        Assert.Null(LiveDiffGate.ExtractHead("</head><head>"));
    }

    // --- DiffOpsAreClientSupported -----------------------------------------

    [Fact]
    public void DiffOpsAreClientSupported_EmptyList_ReturnsTrue() =>
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported([]));

    [Fact]
    public void DiffOpsAreClientSupported_OnlyAttributeAndTextOps_ReturnsTrue()
    {
        List<EditOp> ops =
        [
            Op(EditOpKind.SetAttribute),
            Op(EditOpKind.RemoveAttribute),
            Op(EditOpKind.UpdateText)
        ];

        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Theory]
    [InlineData(EditOpKind.InsertSubtree)]
    [InlineData(EditOpKind.RemoveSubtree)]
    [InlineData(EditOpKind.MoveSubtree)]
    [InlineData(EditOpKind.PermutationBatch)]
    public void DiffOpsAreClientSupported_UntrustedStructuralOp_ReturnsFalse(EditOpKind kind) =>
        Assert.False(LiveDiffGate.DiffOpsAreClientSupported([Op(kind, false)]));

    [Theory]
    [InlineData(EditOpKind.InsertSubtree)]
    [InlineData(EditOpKind.RemoveSubtree)]
    [InlineData(EditOpKind.MoveSubtree)]
    [InlineData(EditOpKind.PermutationBatch)]
    public void DiffOpsAreClientSupported_TrustedStructuralOp_ReturnsTrue(EditOpKind kind)
    {
        // Keyed-matching path marks structural ops Trusted=true; those are safe to apply.
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported([Op(kind, true)]));
    }

    [Fact]
    public void DiffOpsAreClientSupported_OneUntrustedStructuralAmongSafe_ReturnsFalse()
    {
        List<EditOp> ops =
        [
            Op(EditOpKind.SetAttribute),
            Op(EditOpKind.InsertSubtree, false),
            Op(EditOpKind.UpdateText)
        ];

        Assert.False(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Fact]
    public void DiffOpsAreClientSupported_MorphSubtree_ReturnsTrue()
    {
        // MorphSubtree is the Raw-tainted fallback shrunk to one parent's children — a trusted, scoped
        // morph that always ships as a diff (never routes to the full-HTML path), even mixed with the
        // untrusted-structural gate cases around it.
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported([Op(EditOpKind.MorphSubtree, false)]));
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(
        [
            Op(EditOpKind.SetAttribute),
            Op(EditOpKind.MorphSubtree, true),
            Op(EditOpKind.UpdateText)
        ]));
    }
}
