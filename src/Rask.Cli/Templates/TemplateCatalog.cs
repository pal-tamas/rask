namespace Rask.Cli.Templates;

/// <summary>
/// A Rask project template: its friendly <see cref="Key"/> (what the user types after
/// <c>rask new --template</c>), the <c>dotnet new</c> <see cref="ShortName"/> it maps to, and the
/// opt-in feature <see cref="SupportedFlags"/> that template understands.
/// </summary>
internal sealed record TemplateInfo(
    string Key,
    string ShortName,
    string DisplayName,
    IReadOnlySet<string> SupportedFlags);

/// <summary>
/// The set of templates <c>rask new</c> can create, kept in one place so both the command and its
/// tests read the same source of truth. Mirrors the <c>shortName</c>/symbol declarations in
/// <c>src/Rask.Templates/content/*/.template.config/template.json</c>.
/// </summary>
internal static class TemplateCatalog
{
    /// <summary>Feature flags every web template supports.</summary>
    private static readonly string[] WebFlags = ["auth", "pwa", "docker"];

    public static IReadOnlyList<TemplateInfo> All { get; } =
    [
        new("server", "rask-server", "Rask Server app",
            new HashSet<string>(["auth", "pwa", "cqrs", "docker"], StringComparer.Ordinal)),
        new("wasm", "rask-wasm", "Rask browser-WASM SPA",
            new HashSet<string>(WebFlags, StringComparer.Ordinal)),
        new("wasm-hosted", "rask-wasm-hosted", "Rask WASM + ASP.NET host",
            new HashSet<string>(WebFlags, StringComparer.Ordinal)),
        new("native", "rask-native", "Rask native mobile app (iOS + Android)",
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
