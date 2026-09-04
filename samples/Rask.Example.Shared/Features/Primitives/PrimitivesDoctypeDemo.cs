namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesDoctypeDemo : Component
{
    protected override Component? Render() => Span.Class("text-ui-muted")["(emits ", Code["<!DOCTYPE html>"], ")"];
}
