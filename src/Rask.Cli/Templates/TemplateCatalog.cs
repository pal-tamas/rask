namespace Rask.Cli.Templates;

/// <summary>
/// The set of templates <c>rask new</c> can create, kept in one place so both the command and its tests
/// read the same source of truth. Every template is generated directly by <see cref="Scaffolding.ProjectGenerator"/>.
/// </summary>
internal static class TemplateCatalog
{
    /// <summary>Feature flags every web template supports.</summary>
    /// <remarks>
    ///     <c>tailwind</c> and <c>bootstrap</c> are not here: they are the styling AXIS rather than
    ///     features, every template understands them, and the parser handles them before this list is
    ///     consulted.
    ///
    ///     <para>
    ///     <c>localization</c> is here for all three, which it was not between #849 and #846: the
    ///     browser-WASM generators used to accept the flag and scaffold no catalogs and no negotiation, so
    ///     it was struck off both WASM templates rather than left as a silent no-op. They emit both now,
    ///     plus the ICU the browser needs to resolve a culture at all, so the flag means the same thing on
    ///     every template that lists it.
    ///     </para>
    /// </remarks>
    private static readonly string[] WebFlags = ["pwa", "docker"];

    /// <summary>
    /// The database-backed batteries. Available to any template that ships an ASP.NET host to put a
    /// database <em>in</em> — server, wasm-hosted and the front-end templates. A pure browser-WASM SPA has no server to run them on.
    /// </summary>
    private static readonly string[] DatabaseFlags =
        ["cqrs", "data", "jobs", "mail", "cache", "snapshots", "logs", "storage"];

    public static IReadOnlyList<TemplateInfo> All { get; } =
    [
        // Server is just a server. It used to carry a --wasm flag that turned it into a two-half app, and
        // that shape is the wasm-hosted template below now (#1103): a project type answers "what is this
        // app" far better than a yes/no asked after the type has already been chosen.
        //
        // "tests" — the <name>.Tests project — is on this and the wasm template only. wasm-hosted is two
        // halves with two test stories, and has no scaffolded one yet.
        new("server", "Rask Server app",
            new HashSet<string>(
                [.. WebFlags, .. DatabaseFlags, "ops", "push", "tests"],
                StringComparer.Ordinal),
            ShipsLocalization: true),
        // Localized like the server, its list of languages in wwwroot/appsettings.json. English alone ships no ICU, and
        // the build adds it when the list names another language.
        new("wasm", "Rask browser-WASM SPA",
            new HashSet<string>([.. WebFlags, "tests"], StringComparer.Ordinal),
            ShipsLocalization: true),
        // A C# front end on an ASP.NET host. It lives in Client/, the host serves it with MapRaskSpa, and
        // the two halves talk over remote CQRS. "ops": the host is RaskApp's Serve(), which mounts the console.
        // Localized like wasm, from the HOST's appsettings.json, which both halves read.
        new(
            "wasm-hosted",
            "Rask WebAssembly front end + ASP.NET host",
            new HashSet<string>(
                [.. WebFlags, .. DatabaseFlags, "ops", "push"],
                StringComparer.Ordinal),
            ShipsLocalization: true),
        // The TypeScript front-end templates, one per framework: a client in client/ on the same Serve() host,
        // talking to it over generated TypeScript.
        .. SpaFrameworks(),
    ];

    /// <summary>One template per front-end framework, all sharing the same flag set.</summary>
    /// <remarks>
    ///     Derived from <see cref="Scaffolding.SpaFramework.All" /> rather than listed again: two lists of the
    ///     same frameworks is how a template comes to be accepted by the parser and then generate something else.
    ///     "pwa" and "push" are the client's own manifest, service worker and subscription call.
    /// </remarks>
    private static IEnumerable<TemplateInfo> SpaFrameworks() =>
        Scaffolding.SpaFramework.All.Select(framework => new TemplateInfo(
            framework.Key,
            $"Rask {framework.DisplayName} front end + ASP.NET host",
            new HashSet<string>([.. WebFlags, .. DatabaseFlags, "ops", "push"], StringComparer.Ordinal)));

    /// <summary>The default template when none is specified — a server-rendered app.</summary>
    public static TemplateInfo Default => All[0];

    /// <summary>The accepted <c>--template</c> values, for the schema's choice list, help, and completion.</summary>
    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(template => template.Key)];

    public static bool TryGet(string key, out TemplateInfo template)
    {
        var match = All.FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        template = match ?? Default;
        return match is not null;
    }
}
