using Rask.Core;

namespace Rask.Server.Tests.App;

/// <summary>A root that writes the kit's dark-mode script itself, under a key of its own — as an app wired before the host did.</summary>
public sealed partial class OwnAppearanceApp : Component
{
    protected override Component? HeadAssets => [Title["own-appearance"], Ui.AppearanceScript.StorageKey("app.look")];

    protected override Component? Render() => H1["ok"];
}
