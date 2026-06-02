using Rask.Core.Live;
using Rask.Core.ScopedAssets;

namespace Rask.TestSupport;

/// <summary>
///     Base for session tests that mutate process-global live state. The constructor resets
///     the <see cref="ScopedAssetRegistry" /> and pins <see cref="LiveOptions.DiffMode" /> to a
///     subclass-chosen value, replacing the hand-written reset constructors those tests carried.
/// </summary>
public abstract class ResettingTestBase
{
    protected ResettingTestBase(LiveDiffMode diffMode = LiveDiffMode.Auto)
    {
        ScopedAssetRegistry.InvalidateAll();
        LiveOptions.DiffMode = diffMode;
    }
}
