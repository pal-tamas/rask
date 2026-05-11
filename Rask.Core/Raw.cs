namespace Rask.Core;

public sealed class Raw : Component
{
    public Raw(string html) => Value = html;
    internal string Value { get; }

    public override Component Render() => this;
}
