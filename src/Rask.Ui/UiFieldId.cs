using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>The ids a field's parts share. Derived, so the markup is the same on every render.</summary>
internal static class UiFieldId
{
    /// <summary>
    ///     The caller's id, or one from the bound member's path, or from the label — the derived one unique on
    ///     <paramref name="owner" />'s page (<see cref="UiFieldIds" />): a second <c>f-name</c> is <c>f-name-2</c>. The path
    ///     is the whole of it, its indexes left out: <c>() =&gt; f.Opening.Quantity</c> is <c>f-opening-quantity</c>, never
    ///     the <c>f-quantity</c> of a <c>Quantity</c> beside it, and a row's field keeps its id as rows before it go.
    /// </summary>
    /// <param name="owner">The control the id names; none for an id only read, never written on the page.</param>
    /// <param name="id">The id the call site gave, kept as it is.</param>
    /// <param name="bind">The member the control is bound to.</param>
    /// <param name="label">The control's label.</param>
    internal static string Derive(Component? owner, string? id, LambdaExpression? bind, string? label) =>
        id is not null
            ? UiFieldIds.Given(owner, id)
            : UiFieldIds.Derived(owner, "f-" + Slug(BoundName.Written(bind, indexes: false) ?? label ?? "field"));

    /// <summary>One choice of a group: the group's id and the choice's value.</summary>
    internal static string Choice(string groupId, object? value) =>
        groupId + "-" + Slug(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");

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
