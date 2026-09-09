using Rask.Core;
using Rask.Core.Components;

namespace Rask.Tests;

/// <summary>The smallest root a <see cref="RaskApp"/> can serve.</summary>
public sealed partial class TestApp : Component
{
    protected override Component? HeadAssets => Title["rask-tests"];

    protected override Component? Render() => H1["ok"];
}
