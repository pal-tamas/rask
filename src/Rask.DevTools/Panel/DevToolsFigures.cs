using Rask.Core;

namespace Rask.DevTools.Panel;

/// <summary>A tab's figures side by side: two across in a narrow drawer, four once there is room.</summary>
internal sealed partial class DevToolsFigures : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("grid grid-cols-2 gap-3 sm:grid-cols-4")[Children ?? []];
}
