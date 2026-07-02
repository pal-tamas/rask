namespace Rask.Cqrs.Generators;

// Builds the help-link URI an IDE opens when you click a Rask diagnostic id (RASKxxx). Points at
// the per-diagnostic anchor in the docs, so every error/warning links straight to its cause + fix.
// Mirrors DiagnosticHelp in Rask.Generators; duplicated so Rask.Cqrs.Generators stays self-contained.
internal static class DiagnosticHelp
{
    private const string DocBase = "https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md";

    // GitHub lowercases heading anchors, and the docs use "## RASKxxx" headings → "#raskxxx".
    public static string Link(string id) => $"{DocBase}#{id.ToLowerInvariant()}";
}
