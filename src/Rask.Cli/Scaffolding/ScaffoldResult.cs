namespace Rask.Cli.Scaffolding;

internal sealed record ScaffoldResult(IReadOnlyList<ScaffoldFile> Files, string? Notes = null)
{
    /// <summary>Packages the generated code references, added to the project via <c>dotnet add package</c>.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
