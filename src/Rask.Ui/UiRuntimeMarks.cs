namespace Rask;

/// <summary>The attributes by which a kit control tells the runtime what a part of it is to the page around it.</summary>
/// <remarks>The runtime's side of each is in <c>docs/js-interop-runtime.md</c>.</remarks>
internal static class UiRuntimeMarks
{
    /// <summary>
    ///     On a field the reader types or chooses in that holds none of a form's data — a list's search, a
    ///     calendar's month, a box the kit measures into: a form that asks before it is left (<c>ConfirmLeave</c>)
    ///     does not count what happens in it (<c>rask-leave.ts</c>).
    /// </summary>
    internal const string Unguarded = "data-rask-unguarded";

    /// <summary>The same mark as the one pair an <c>Attributes(…)</c> call takes.</summary>
    internal static readonly (string, string?) NotFormData = (Unguarded, null);
}
