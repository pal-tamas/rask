using Rask.Core;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

namespace Rask.TestSupport;

/// <summary>
///     Base for session tests that touch process-wide live state. The constructor resets the
///     process-global <see cref="ScopedAssetRegistry" /> (why these subclasses run under a
///     serialized collection) and records the wire-payload shape the subclass wants. DiffMode is
///     no longer a static — it is <b>per session</b> now — so subclasses pass <see cref="DiffMode" />
///     to the harness that builds their session (e.g. WasmSessionHarness) rather than relying on a
///     shared global.
/// </summary>
/// <remarks>
///     Derives from <see cref="RaskMarkup" /> so its subclasses can name markup. A test class reaches
///     the builder surface by deriving from <c>RaskMarkup</c>, and C# has one base slot — so a shared
///     test base has to pass it on, or none of the 14 classes under it can. That is one edit here and
///     none in any of them; a base you do <i>not</i> own is the case with no such edit.
/// </remarks>
public abstract partial class ResettingTestBase : RaskMarkup
{
    /// <summary>The wire-payload shape this test's sessions should render with. Hand it to the session factory.</summary>
    protected LiveDiffMode DiffMode { get; }

    protected ResettingTestBase(LiveDiffMode diffMode = LiveDiffMode.Auto)
    {
        DiffMode = diffMode;
        ScopedAssetRegistry.InvalidateAll();
    }
}
