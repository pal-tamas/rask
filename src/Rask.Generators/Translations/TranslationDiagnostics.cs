using Microsoft.CodeAnalysis;

namespace Rask.Generators.Translations;

// Two ids for the whole subsystem, with the specific cause carried in the message.
//
// That is the RASK003 pattern ("Route template '{0}' on '{1}' is malformed: {2}") and it is a
// deliberate economy: RASK051 and RASK052 are the last free ids below the retired block, and a
// caller reading "catalog X is malformed: duplicate key 'Y'" learns exactly as much as they would
// from a dedicated id, while an .editorconfig rule still has something coherent to target.
internal static class TranslationDiagnostics
{
    public static readonly DiagnosticDescriptor Malformed = new(
        "RASK051",
        "Translation catalog is malformed",
        "Translation catalog '{0}' is malformed: {1}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A translation catalog is a JSON object whose values are text or further objects, named "
                     + "Resources/{Family}.{culture}.json. This one either cannot be read, or describes strings "
                     + "that would fail at runtime — a placeholder set that disagrees with the neutral catalog "
                     + "throws FormatException the first time the string is used, so it is refused at build time "
                     + "instead. Fix the reported cause in the file named by the message.",
        helpLinkUri: DiagnosticHelp.Link("RASK051"));

    public static readonly DiagnosticDescriptor Disagrees = new(
        "RASK052",
        "Translation catalog disagrees with the neutral catalog",
        "Translation catalog '{0}' disagrees with '{1}': {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The neutral catalog defines which keys exist; a translation supplies the text for them. "
                     + "This one is missing a key, or carries one the neutral catalog does not define. A missing "
                     + "translation is a warning rather than an error because a partly translated app is the "
                     + "normal state of every real project — the neutral text is used until it is filled in. Set "
                     + "dotnet_diagnostic.RASK052.severity = error to gate releases on complete translations, or "
                     + "= none while translating.",
        helpLinkUri: DiagnosticHelp.Link("RASK052"));
}
