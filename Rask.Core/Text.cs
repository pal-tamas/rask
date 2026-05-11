namespace Rask.Core;

public sealed class Text : Component
{
    public Text(string value) => Value = value;
    internal string Value { get; }

    public override Component Render() => this;
}
