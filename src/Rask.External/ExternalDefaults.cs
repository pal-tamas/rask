namespace Rask.External;

/// <summary>
///     The names the two halves of an island have to agree on: the C# that renders the host element,
///     and <c>rask-external.js</c> that finds and mounts it.
/// </summary>
/// <remarks>
///     Constants rather than a markup helper on purpose. The generated code lives inside the island
///     component, which is already a markup host, so it can write the <c>&lt;script&gt;</c> itself —
///     what it cannot do is invent these strings twice and stay in step with the client runtime.
/// </remarks>
public static class ExternalDefaults
{
    /// <summary>
    ///     The element an island renders.
    /// </summary>
    /// <remarks>
    ///     A custom element name (it contains a hyphen), so the browser parses it as
    ///     <c>HTMLUnknownElement</c> rather than trying to interpret it, and CSS can address islands as
    ///     a class of thing. Deliberately not a <c>&lt;div&gt;</c> with a marker class: an island is not
    ///     a div, and the distinction shows up in devtools where people need it.
    /// </remarks>
    public const string HostTag = "rask-external";

    /// <summary>The island's registered name, which the client resolves to a module.</summary>
    public const string NameAttribute = "name";

    /// <summary>The serialized props. The one thing that crosses the diff boundary.</summary>
    public const string PropsAttribute = "props";

    /// <summary>When to mount. Omitted entirely for the default, which keeps the markup quiet.</summary>
    public const string HydrateAttribute = "hydrate";

    /// <summary>The module specifier, when it is not derivable from the island's name.</summary>
    public const string ModuleAttribute = "module";

    /// <summary>Which adapter mounts it.</summary>
    public const string RuntimeAttribute = "runtime";

    /// <summary>
    ///     Where the client runtime is served from — a static web asset of this package, so the URL is
    ///     the same in-repo and from the packed NuGet.
    /// </summary>
    public const string RuntimeScriptUrl = "/_content/Rask.External/rask-external.js";

    /// <summary>The wire spelling of a hydration policy, or null for the default.</summary>
    /// <remarks>
    ///     Null for <see cref="ExternalHydration.Load" /> so the common case writes no attribute at all —
    ///     the client already treats a missing policy as "mount when the chunk is ready".
    /// </remarks>
    public static string? Wire(ExternalHydration hydration) => hydration switch
    {
        ExternalHydration.Idle => "idle",
        ExternalHydration.Visible => "visible",
        ExternalHydration.None => "none",
        _ => null,
    };
}
