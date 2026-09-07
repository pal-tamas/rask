using Rask.Core;
using Rask.Html.Components;

namespace Rask.ScopedAssets.Fixture;

/// <summary>
///     A component with a scoped stylesheet and a scoped script beside it, so the generator emits both
///     registration classes into this assembly. Its markup is irrelevant — what is under test is the
///     bake, not the render.
/// </summary>
public sealed partial class ScopedWidget : Component
{
    protected override Component? Render() => Div.Class("scoped-widget")["scoped"];
}
