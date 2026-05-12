namespace Rask.Core;

public sealed class Raw(string html) : Component
{
    internal string Value { get; } = html;

    protected override Component Render() => this;
}
