namespace Rask.Cli.Scaffolding;

/// <summary>
///     One front-end framework <c>rask new</c> scaffolds a TypeScript client for. <see cref="Key" /> is the
///     <c>--template</c> value and the name of its committed tree under <c>src/Rask.Templates/</c>.
/// </summary>
internal sealed record SpaFramework(string Key, string DisplayName)
{
    public static readonly SpaFramework React = new("react", "React");

    /// <summary>
    ///     Every framework with a committed tree. The catalog, the generator's dispatch and the refusals all read
    ///     this list, so a framework is added by committing its tree and adding its row here.
    /// </summary>
    public static IReadOnlyList<SpaFramework> All { get; } = [React];

    public static bool TryGet(string key, out SpaFramework framework)
    {
        var match = All.FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        framework = match ?? React;
        return match is not null;
    }
}
