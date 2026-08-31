namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesDoctypeDemo : Component
{
    protected override Component? Render() => Span.Class("text-slate-500 dark:text-slate-400")["(emits ", Code["<!DOCTYPE html>"], ")"];
}
