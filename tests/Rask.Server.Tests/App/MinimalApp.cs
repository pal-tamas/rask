using Rask.Core;

namespace Rask.Server.Tests.App;

/// <summary>The smallest root a <see cref="RaskApp"/> can serve.</summary>
public sealed partial class MinimalApp : Component
{
    protected override Component? HeadAssets => Title["rask-server-tests"];

    protected override Component? Render() => H1["ok"];
}
