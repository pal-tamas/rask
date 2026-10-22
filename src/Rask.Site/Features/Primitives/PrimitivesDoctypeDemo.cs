namespace Rask.Site.Features;

public sealed partial class PrimitivesDoctypeDemo : Component
{
    protected override Component? Render() => Span.Class("text-zinc-500 dark:text-white/70")["(emits ", Code["<!DOCTYPE html>"], ")"];
}
