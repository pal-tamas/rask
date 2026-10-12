using System.ComponentModel;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     What a generated form model is asked before it becomes an aggregate: the pieces the generated
///     <c>Create(model)</c> and <c>Update(id, model)</c> — and the rules a form runs by itself — are built from.
/// </summary>
/// <remarks>
///     Called by generated code, which lives in the application's assembly and so cannot reach an
///     <c>internal</c> member. Not an API to write against: give a value object a <c>Validate</c> and the model
///     asks it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedModelRules
{
    /// <summary>
    ///     What an unfilled field says when its property cannot be empty and its type has no words of its own
    ///     for that — a number, a date, an id: there is no empty value to ask the value object about.
    /// </summary>
    /// <remarks>
    ///     PROVISIONAL: one English sentence. It belongs in Rask's strings catalog (a <c>RaskString</c> key with
    ///     the built-in translations), which lives in Rask.Core; until that key exists this constant stands in.
    /// </remarks>
    public const string RequiredMessage = "This field is required.";

    private static readonly string[] RequiredOnly = [RequiredMessage];

    /// <summary>The messages of an unfilled field whose type cannot be asked: the one sentence.</summary>
    /// <returns><see cref="RequiredMessage" />, alone.</returns>
    public static IEnumerable<string> Required() => RequiredOnly;

    /// <summary>The messages of an unfilled field that must be filled: <paramref name="sentence" />, alone.</summary>
    /// <param name="sentence">The framework's sentence, in the reader's language.</param>
    /// <returns><paramref name="sentence" />, alone.</returns>
    public static IEnumerable<string> Required(string sentence) => [sentence];

    /// <summary>
    ///     What a value object said about its empty value — or, when it accepts that,
    ///     <paramref name="sentence" />.
    /// </summary>
    /// <param name="said">The messages the value object's rule gave for its empty value.</param>
    /// <param name="sentence">The framework's sentence, in the reader's language.</param>
    /// <returns><paramref name="said" /> when it says anything; otherwise <paramref name="sentence" />.</returns>
    public static IEnumerable<string> OrRequired(IEnumerable<string>? said, string sentence)
    {
        var own = OrRequired(said);
        return ReferenceEquals(own, RequiredOnly) ? [sentence] : own;
    }

    /// <summary>
    ///     What a value object said about its empty value — or, when it accepts that, the one sentence: the
    ///     property cannot be empty, and a field that holds nothing at all holds no value to keep.
    /// </summary>
    /// <param name="said">The messages the value object's rule gave for its empty value.</param>
    /// <returns><paramref name="said" /> when it says anything; otherwise <see cref="RequiredMessage" />.</returns>
    public static IEnumerable<string> OrRequired(IEnumerable<string>? said)
    {
        IReadOnlyCollection<string> messages = said switch
        {
            null => [],
            IReadOnlyCollection<string> counted => counted,
            _ => [.. said],
        };

        return messages.Count > 0 ? messages : RequiredOnly;
    }

    /// <summary>Adds one failure for each of <paramref name="messages" />, under <paramref name="field" />.</summary>
    /// <param name="failures">The failures so far, or <see langword="null" /> for none yet.</param>
    /// <param name="path">What leads to the model from the root — empty, or <c>Lines[2].</c>.</param>
    /// <param name="field">The field, as its model names it.</param>
    /// <param name="messages">What the rule said.</param>
    /// <returns>The failures, still <see langword="null" /> when nothing was said.</returns>
    public static IList<FieldFailure>? Told(
        IList<FieldFailure>? failures, string path, string field, IEnumerable<string>? messages)
    {
        if (messages is null)
        {
            return failures;
        }

        foreach (var message in messages)
        {
            failures = Told(failures, path, field, message);
        }

        return failures;
    }

    /// <summary>Adds one failure saying <paramref name="message" /> under <paramref name="field" />.</summary>
    /// <param name="failures">The failures so far, or <see langword="null" /> for none yet.</param>
    /// <param name="path">What leads to the model from the root — empty, or <c>Lines[2].</c>.</param>
    /// <param name="field">The field, as its model names it.</param>
    /// <param name="message">What to say.</param>
    /// <returns>The failures, with this one.</returns>
    public static IList<FieldFailure> Told(IList<FieldFailure>? failures, string path, string field, string message)
    {
        ArgumentNullException.ThrowIfNull(path);

        failures ??= new List<FieldFailure>();
        failures.Add(new FieldFailure(message, [path.Length == 0 ? field : path + field]));
        return failures;
    }

    /// <summary>The exception a write throws for a model its rules refused, before anything is written.</summary>
    /// <param name="failures">What was wrong, each under the field a form names it by.</param>
    /// <returns>The exception to throw.</returns>
    public static RaskValidationException Refused(IList<FieldFailure> failures) => new([.. failures]);
}
