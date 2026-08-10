namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesDoctypeDemo : Component
{
    protected override Component? Render() => Span.Class("text-secondary")["(emits ", Code["<!DOCTYPE html>"], ")"];
}
