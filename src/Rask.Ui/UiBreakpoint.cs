namespace Rask.Ui;

/// <summary>A width the layout changes at — Tailwind's own breakpoints, named the way Tailwind names them.</summary>
/// <remarks>
/// <c>Sm</c> is 40rem (640px), <c>Md</c> 48rem, <c>Lg</c> 64rem and <c>Xl</c> 80rem. Closed, like every other
/// axis in the kit, because each member maps to a complete class literal that the compiled sheet can be checked
/// for — a breakpoint spelled into a class at runtime is a class the sheet never received.
/// </remarks>
public enum UiBreakpoint
{
    Sm,
    Md,
    Lg,
    Xl,
}
