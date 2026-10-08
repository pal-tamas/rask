namespace Rask;

/// <summary>The attributes a component writes that depend on a prop: one that has no value is not written.</summary>
/// <remarks>
/// <c>Attributes</c> writes a pair with no value as a bare attribute, which is right for a marker and wrong
/// for a <c>title</c> or an <c>aria-label</c> nobody gave.
/// </remarks>
internal static class UiMarks
{
    /// <summary>The pairs that have a value, in the order given.</summary>
    public static (string, string?)[] Present(params (string Name, string? Value)[] pairs) =>
        [.. pairs.Where(pair => pair.Value is not null)];
}
