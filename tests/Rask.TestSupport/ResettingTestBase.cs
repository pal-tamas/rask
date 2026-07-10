using Rask.Core.Live;
using Rask.Core.ScopedAssets;

namespace Rask.TestSupport;

/// <summary>
///     Base for session tests that touch process-wide live state. The constructor resets the
///     process-global <see cref="ScopedAssetRegistry" /> (why these subclasses run under a
///     serialized collection) and records the wire-payload shape the subclass wants. DiffMode is
///     no longer a static — it is <b>per session</b> now — so subclasses pass <see cref="DiffMode" />
///     to the harness that builds their session (NativeAppHost / WasmSessionHarness) rather than
///     relying on a shared global.
/// </summary>
public abstract class ResettingTestBase
{
    /// <summary>The wire-payload shape this test's sessions should render with. Hand it to the session factory.</summary>
    protected LiveDiffMode DiffMode { get; }

    protected ResettingTestBase(LiveDiffMode diffMode = LiveDiffMode.Auto)
    {
        DiffMode = diffMode;
        ScopedAssetRegistry.InvalidateAll();
    }
}
