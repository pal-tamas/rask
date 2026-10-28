namespace Rask;

/// <summary>
/// Joins the parts of a class list where no chain step takes them: a <c>ResolveClass()</c> override, a named
/// look that is itself one part of a bigger list, or a string handed to a non-chain sink.
/// </summary>
/// <remarks>
/// <para>
/// Where an element is being built, pass the parts to its <c>.Class(a, b, c)</c> step instead: it joins by the
/// same rules (<see cref="BuilderRuntime.JoinClasses" />). This differs only in returning the empty string, not
/// <c>null</c>, when every part is blank.
/// </para>
/// <para>
/// This does no other work. In particular it never BUILDS a class name, only joins ones already spelled
/// out as whole literals: Tailwind emits a class only where it can see the
/// literal, so a name assembled at runtime would be absent from the sheet and its component would render
/// unstyled with nothing reporting it.
/// </para>
/// </remarks>
internal static class UiClass
{
    internal static string Compose(params ReadOnlySpan<string?> parts) => BuilderRuntime.JoinClasses(parts) ?? string.Empty;
}
