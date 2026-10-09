using Rask.Core.Live;

namespace Rask.Testing;

/// <summary>One page's renders, seen as a live session sees them: the frames of a walk, then its markup.</summary>
internal interface IRenderWatch
{
    /// <summary>Where the walk about to run writes its frames.</summary>
    FrameWriter Frames();

    /// <summary>The walk is over and produced <paramref name="html" />.</summary>
    void Rendered(string html);
}
