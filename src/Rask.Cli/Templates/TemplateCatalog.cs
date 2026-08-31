namespace Rask.Cli.Templates;

/// <summary>
/// A Rask project template: its friendly <see cref="Key"/> (what the user types after
/// <c>rask new --template</c>), a human <see cref="DisplayName"/>, and the opt-in feature
/// <see cref="SupportedFlags"/> that template understands.
/// </summary>
internal sealed record TemplateInfo(
    string Key,
    string DisplayName,
    IReadOnlySet<string> SupportedFlags,
    IReadOnlySet<string>? OptIn = null)
{
    private static readonly IReadOnlySet<string> NoneOptIn = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Batteries this template supports but does <em>not</em> switch on by default — the exceptions to
    /// "everything the template supports".
    /// </summary>
    /// <remarks>
    /// There is one, and it earns the exception by costing something the user can measure: localization on
    /// a browser-WASM template needs ICU in the bundle, which is about a megabyte of extra download
    /// (+32% on the showcase, measured brotli-to-brotli on a published trimmed build). A battery is
    /// wiring you would otherwise write by hand; a third more download for a feature most apps never use
    /// is an opinion about the app, and the two things are exactly what auth and styling are separated
    /// for. The framework's own <c>RaskGlobalization</c> default says the same thing.
    ///
    /// <para>
    /// Supported, so the flag still does real work — this is not the <c>--template native</c> shape where
    /// a flag is accepted and disregarded. On these templates <c>--culture &lt;tag&gt;</c> is what turns
    /// it on, and <c>--no-localization</c> is refused as already-true rather than accepted.
    /// </para>
    /// </remarks>
    public IReadOnlySet<string> OptInFlags { get; } = OptIn ?? NoneOptIn;
}

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
    private static readonly string[] WebFlags = ["auth", "pwa", "docker", "localization"];

    /// <summary>
    /// The database-backed batteries. Available to any template that ships an ASP.NET host to put a
    /// database <em>in</em> — the server template, and the wasm-hosted template's <c>.Server</c> project.
    /// A pure browser-WASM SPA has no server to run them on.
    /// </summary>
    private static readonly string[] DatabaseFlags =
        ["cqrs", "data", "jobs", "mail", "cache", "outbox", "snapshots", "logs", "ops"];

    /// <summary>The browser templates ship localization, but only when asked — see <see cref="TemplateInfo.OptInFlags"/>.</summary>
    private static readonly IReadOnlySet<string> LocalizationIsOptIn =
        new HashSet<string>(["localization"], StringComparer.Ordinal);

    public static IReadOnlyList<TemplateInfo> All { get; } =
    [
        // --wasm is listed on this template alone. It is the one-project build: the app is authored once
        // as a server app, and publish emits a browser bundle beside it from the same sources. The other
        // templates either already ARE the browser half or carry a hand-written one.
        new("server", "Rask Server app",
            new HashSet<string>(
                [.. WebFlags, .. DatabaseFlags, "push", "wasm"],
                StringComparer.Ordinal)),
        new("wasm", "Rask browser-WASM SPA",
            new HashSet<string>(WebFlags, StringComparer.Ordinal),
            LocalizationIsOptIn),
        // The TypeScript front-end templates, one per framework: a client on an ASP.NET host, talking to
        // it over generated TypeScript. CQRS is listed but never optional here — the wire IS the template,
        // so the generator forces it on and --no-cqrs is refused rather than silently ignored.
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
            // "pwa" and "push" but not "auth": the PWA half is the client's own manifest, service worker
            // and subscription call, none of which need a login. Auth would need a sign-in flow written in
            // the framework's own idiom, which the template does not scaffold yet.
            new HashSet<string>([.. DatabaseFlags, "docker", "pwa", "push"], StringComparer.Ordinal)));

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
