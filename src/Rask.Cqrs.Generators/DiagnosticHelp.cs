namespace Rask.Cqrs.Generators;

// Builds the help-link URI an IDE opens when you click a Rask diagnostic id (RASKxxx). Points at
// the per-diagnostic anchor in the docs, so every error/warning links straight to its cause + fix.
// Mirrors DiagnosticHelp in Rask.Generators; duplicated so Rask.Cqrs.Generators stays self-contained.
internal static class DiagnosticHelp
{
    // The single category every RASKxxx descriptor reports under.
    //
    // There used to be two, split by which kind of thing produced the diagnostic: the incremental
    // generators used "Rask.Generators" and the analyzers used "Usage". That is an implementation
    // detail of this repo, and it leaked all the way out to the consumer — a category is what an
    // .editorconfig rule or an IDE's "group by category" keys on, so any attempt to treat the family
    // as a family (`dotnet_analyzer_diagnostic.category-Rask.severity = …`) silently caught half of
    // it (#609). One family, one category.
    public const string Category = "Rask";

    private const string DocBase = "https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md";

    // GitHub lowercases heading anchors, and the docs use "## RASKxxx" headings → "#raskxxx".
    public static string Link(string id) => $"{DocBase}#{id.ToLowerInvariant()}";
}
