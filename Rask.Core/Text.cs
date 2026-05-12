namespace Rask.Core;

public sealed class Text(string value) : Component
{
    internal string Value { get; } = value;

    protected override Component Render() => this;
}
