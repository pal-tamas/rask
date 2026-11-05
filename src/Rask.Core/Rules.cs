using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     The sentences a form's own rules answer with, in the reader's language: what a value object's
///     <c>Validate</c> or a control's <c>.Validate(…)</c> returns, so no two pages say the same thing differently.
/// </summary>
/// <remarks>
///     <code>
///     public readonly record struct DriverName(string Value)
///     {
///         public static IEnumerable&lt;string&gt; Validate(string value) => Rules.Text(value, 100);
///     }
///
///     Ui.Input.Bind(() => model.Note).Validate(note => Rules.Optional(note, 500))
///     </code>
///     <para>
///         Every sentence is a <c>RaskString</c> key, so it reads the app's <c>RaskStrings</c> catalog, then the
///         kit's own Hungarian, then the English: <see cref="Required" /> is
///         <see cref="RaskString.ValidationRequired" />, <see cref="TooLong" /> is
///         <see cref="RaskString.ValidationTooLong" />. The same keys word a <c>[Required]</c> or a
///         <c>[StringLength]</c> on a model, so a rule written here and an attribute say one thing.
///     </para>
/// </remarks>
public static class Rules
{
    /// <summary>What a field that must be filled says when it is left empty: "This field is required."</summary>
    public static string Required => RaskStrings.Get(RaskString.ValidationRequired, "This field is required.");

    /// <summary>What a text longer than <paramref name="max" /> characters says: "Enter at most 100 characters."</summary>
    /// <param name="max">The most characters the text may hold.</param>
    /// <returns>The sentence, with <paramref name="max" /> written into it.</returns>
    public static string TooLong(int max) => RaskStrings.Get(RaskString.ValidationTooLong, "Enter at most {0} characters.", max);

    /// <summary>
    ///     What is wrong with a text that must be filled and may hold at most <paramref name="max" /> characters:
    ///     <see cref="Required" /> for nothing or only spaces, <see cref="TooLong" /> past <paramref name="max" />,
    ///     nothing otherwise.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="max">The most characters it may hold.</param>
    /// <returns>The one sentence that applies, or none.</returns>
    public static IEnumerable<string> Text(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? [Required] : Optional(value, max);

    /// <summary>
    ///     What is wrong with a text that may be left empty and may hold at most <paramref name="max" /> characters:
    ///     <see cref="TooLong" /> past <paramref name="max" />, nothing otherwise.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="max">The most characters it may hold.</param>
    /// <returns>The one sentence that applies, or none.</returns>
    public static IEnumerable<string> Optional(string? value, int max) =>
        value is { } text && text.Length > max ? [TooLong(max)] : [];

    /// <summary>
    ///     What is wrong with a value a form requires though its type may be empty — a number, a date, an id
    ///     held as <c>int?</c>: <see cref="Required" /> when nothing is given, nothing otherwise.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <returns>The one sentence that applies, or none.</returns>
    public static IEnumerable<string> Given<T>(T? value)
        where T : struct =>
        value is null ? [Required] : [];
}
