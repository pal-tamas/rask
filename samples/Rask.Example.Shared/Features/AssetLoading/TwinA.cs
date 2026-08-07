namespace Rask.Example.Shared.Features;

/// <summary>
///     Twin A — paired with <see cref="TwinB" /> to demonstrate two components with
///     different scoped CSS each get their own content-addressed URL. Two
///     <c>&lt;link&gt;</c> tags in <c>&lt;head&gt;</c>, two distinct hashes.
/// </summary>
public sealed partial class TwinA : Component
{
    protected override Component? Render() =>
        Div(Class: "twin-tag")["Twin A — independent hash"];
}
