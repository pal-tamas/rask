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
    private static readonly string[] WebFlags = ["auth", "pwa", "docker", "localization"];

    /// <summary>
    /// The database-backed batteries. Available to any template that ships an ASP.NET host to put a
    /// database <em>in</em> — the server template, and the wasm-hosted template's <c>.Server</c> project.
    /// A pure browser-WASM SPA has no server to run them on.
    /// </summary>
    private static readonly string[] DatabaseFlags =
        ["cqrs", "data", "jobs", "mail", "cache", "outbox", "snapshots", "logs", "ops", "all-batteries"];

    public static IReadOnlyList<TemplateInfo> All { get; } =
    [
        new("server", "Rask Server app",
            new HashSet<string>(
                [.. WebFlags, .. DatabaseFlags, "push"],
                StringComparer.Ordinal)),
        new("wasm", "Rask browser-WASM SPA",
            new HashSet<string>(WebFlags, StringComparer.Ordinal)),
        // Same batteries as server, minus --push: Web Push needs the subscribe endpoints AND a service
        // worker that posts to them, and in this template those live in two different projects. It is a
        // real feature rather than a wiring gap, so it is left out rather than half-scaffolded.
        //
        // --cqrs means more here than on the server template: this is the one template with a server half
        // to dispatch TO, so it wires remote dispatch across both projects rather than only registering the
        // mediator. On 'wasm' there is no host in the solution, so the flag would name a destination that
        // isn't there.
        new("wasm-hosted", "Rask WASM + ASP.NET host",
            new HashSet<string>(
                [.. WebFlags, .. DatabaseFlags],
                StringComparer.Ordinal)),
    ];

    /// <summary>The default template when none is specified — a server-rendered app.</summary>
    public static TemplateInfo Default => All[0];

    /// <summary>The accepted <c>--template</c> values, for the schema's choice list, help, and completion.</summary>
    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(template => template.Key)];

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
