namespace Rask.Cli.Scaffolding;

/// <summary>
///     One front-end framework <c>rask new</c> scaffolds a TypeScript client for. <see cref="Key" /> is the
///     <c>--template</c> value and the name of its committed tree under <c>src/Rask.Templates/</c>.
/// </summary>
internal sealed record SpaFramework(string Key, string DisplayName)
{
    public static readonly SpaFramework React = new("react", "React");
    public static readonly SpaFramework Preact = new("preact", "Preact");
    public static readonly SpaFramework Vue = new("vue", "Vue");

    /// <summary>The one client not built on Vite's own config: <c>ng serve</c>, its port and its proxy file.</summary>
    public static readonly SpaFramework Angular = new("angular", "Angular") { DevServerUrl = LocalDevServers.Angular };

    public static readonly SpaFramework Solid = new("solid", "Solid");
    public static readonly SpaFramework Svelte = new("svelte", "Svelte");
    public static readonly SpaFramework Lit = new("lit", "Lit");

    /// <summary>
    ///     Where the client's dev server listens — what the tree's csproj names as <c>RaskSpaDevServerUrl</c>, and
    ///     what the next steps print.
    /// </summary>
    public string DevServerUrl { get; init; } = LocalDevServers.Vite;

    /// <summary>
    ///     Every framework with a committed tree. The catalog, the generator's dispatch and the refusals all read
    ///     this list, so a framework is added by committing its tree and adding its row here.
    /// </summary>
    public static IReadOnlyList<SpaFramework> All { get; } = [React, Preact, Vue, Angular, Solid, Svelte, Lit];

    public static bool TryGet(string key, out SpaFramework framework)
    {
        var match = All.FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        framework = match ?? React;
        return match is not null;
    }
}
