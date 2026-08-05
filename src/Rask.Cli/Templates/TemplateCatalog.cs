namespace Rask.Cli.Templates;

/// <summary>
/// A Rask project template: its friendly <see cref="Key"/> (what the user types after
/// <c>rask new --template</c>), a human <see cref="DisplayName"/>, and the opt-in feature
/// <see cref="SupportedFlags"/> that template understands.
/// </summary>
internal sealed record TemplateInfo(
    string Key,
    string DisplayName,
    IReadOnlySet<string> SupportedFlags);

/// <summary>
/// The set of templates <c>rask new</c> can create, kept in one place so both the command and its tests
/// read the same source of truth. Every template is generated directly by <see cref="Scaffolding.ProjectGenerator"/>.
/// </summary>
internal static class TemplateCatalog
{
    /// <summary>Feature flags every web template supports.</summary>
    private static readonly string[] WebFlags = ["auth", "pwa", "docker"];

    public static IReadOnlyList<TemplateInfo> All { get; } =
    [
        // The server template is the only one with a database, so every DB-backed battery is server-only.
        new("server", "Rask Server app",
            new HashSet<string>(
                [
                    "auth", "pwa", "cqrs", "data", "docker",
                    "jobs", "mail", "cache", "outbox", "push", "snapshots", "logs", "ops", "all-batteries",
                ],
                StringComparer.Ordinal)),
        new("wasm", "Rask browser-WASM SPA",
            new HashSet<string>(WebFlags, StringComparer.Ordinal)),
        new("wasm-hosted", "Rask WASM + ASP.NET host",
            new HashSet<string>(WebFlags, StringComparer.Ordinal)),
        new("native", "Rask native mobile app (iOS + Android)",
            new HashSet<string>(StringComparer.Ordinal)),
    ];

    /// <summary>The default template when none is specified — a server-rendered app.</summary>
    public static TemplateInfo Default => All[0];

    public static bool TryGet(string key, out TemplateInfo template)
    {
        foreach (var candidate in All)
        {
            if (candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                template = candidate;
                return true;
            }
        }

        template = Default;
        return false;
    }
}
