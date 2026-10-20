using static Rask.Markup;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A sentence of the UI tab's prose whose spans between backticks are code, as in Markdown: a rule is written once,
///     as a sentence, without a chain of text and code pieces per line.
/// </summary>
internal static class UiDocsProse
{
    /// <summary>The pieces of <paramref name="text" />: its words as text and its backticked spans as code.</summary>
    /// <remarks>A span of code breaks where it must, so a long chain never pushes a phone's page sideways.</remarks>
    public static IEnumerable<Component> Of(string text) =>
        text.Split('`').Select((part, index) =>
            index % 2 == 1 ? Code.Class("[overflow-wrap:anywhere]")[part] : (Component)Text.Value(part));
}
