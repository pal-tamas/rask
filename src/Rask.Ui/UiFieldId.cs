using System.Linq.Expressions;

namespace Rask;

/// <summary>The ids a field's parts share. Derived, so the markup is the same on every render.</summary>
internal static class UiFieldId
{
    /// <summary>The caller's id, or one from the bound member's name, or from the label.</summary>
    internal static string Derive(string? id, LambdaExpression? bind, string? label) =>
        id ?? "f-" + Slug((bind?.Body as MemberExpression)?.Member.Name ?? label ?? "field");

    /// <summary>The id of a control nothing names, from its instance number: unique on the page.</summary>
    internal static string Own(int instance) => "f-field-" + instance.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static string Label(string controlId) => controlId + "-label";

    internal static string Description(string controlId) => controlId + "-description";

    internal static string Error(string controlId) => controlId + "-error";

    private static string Slug(string text) =>
        string.Create(text.Length, text, static (slug, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                slug[i] = char.IsAsciiLetterOrDigit(source[i]) ? char.ToLowerInvariant(source[i]) : '-';
            }
        }).Trim('-');
}
