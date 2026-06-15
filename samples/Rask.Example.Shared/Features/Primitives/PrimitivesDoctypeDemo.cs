namespace Rask.Example.Shared.Features;

public sealed class PrimitivesDoctypeDemo : Component
{
    protected override RenderResult Render() => Span(Class: "text-secondary")["(emits ", Code()["<!DOCTYPE html>"], ")"];
}
