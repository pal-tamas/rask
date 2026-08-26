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
        new("native", "Rask native mobile app (iOS + Android)",
            new HashSet<string>(StringComparer.Ordinal)),
        // The TypeScript front-end templates, one per framework: a client on an ASP.NET host, talking to
        // it over generated TypeScript. --cqrs is not listed because it is not optional here — the wire IS
        // the template, and a flag you cannot turn off is a worse thing to advertise than no flag at all.
        //
        // --auth and --pwa are left out rather than half-scaffolded: both need work on the CLIENT side
        // (a login flow, a service worker through vite-plugin-pwa) that these templates do not write yet.
        // --push needs both.
        //
        // The set matches the frameworks TanStack Query ships an adapter for, because the adapter is what
        // makes the generated contracts worth having — everything below the call site is the same wire.
        .. SpaFrameworks(),
    ];

    /// <summary>One template per front-end framework, all sharing the same flag set.</summary>
    /// <remarks>
    ///     Derived from <see cref="Scaffolding.SpaFramework.All" /> rather than listed again here. Two
    ///     hand-maintained lists of the same frameworks is exactly how a template comes to be accepted by
    ///     the parser and then generate something else — which is what <c>--template native</c> did after
    ///     the native host was deleted.
    /// </remarks>
    private static IEnumerable<TemplateInfo> SpaFrameworks() =>
        Scaffolding.SpaFramework.All.Select(framework => new TemplateInfo(
            framework.Key,
            $"Rask {framework.DisplayName} front end + ASP.NET host",
            new HashSet<string>([.. DatabaseFlags, "docker"], StringComparer.Ordinal)));

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
