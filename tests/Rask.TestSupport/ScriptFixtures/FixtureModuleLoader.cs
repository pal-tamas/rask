using Jint;
using Jint.Runtime.Modules;

namespace Rask.TestSupport.ScriptFixtures;

/// <summary>
///     Resolves a fixture's <c>import</c>s to files: by absolute path, or relative to the importing module, with the
///     modules the fixture wrote itself (<see cref="Add" />) answered ahead of the disk.
/// </summary>
/// <remarks>
///     The in-memory modules are how a fixture stands a shipped file up beside stubs of its neighbours — the boot
///     script next to a fake <c>_framework/dotnet.js</c> — without a temporary directory to create and clean up.
///     Bare specifiers are refused: a fixture is bundled, so one that survives to run time is a mistake.
/// </remarks>
internal sealed class FixtureModuleLoader : ModuleLoader
{
    private readonly Dictionary<string, string> _written = new(StringComparer.Ordinal);

    public void Add(string path, string source) => _written[Path.GetFullPath(path)] = source;

    public override ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest)
    {
        var specifier = moduleRequest.Specifier;
        var relative = specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);
        if (!relative && !Path.IsPathFullyQualified(specifier))
        {
            throw new ModuleResolutionException(
                $"'{specifier}' is neither a path nor relative to one. A fixture is bundled, so a bare import left at run "
                + "time has nothing to resolve to.",
                specifier, referencingModuleLocation, filePath: null);
        }

        var path = relative
            ? Path.GetFullPath(specifier, Path.GetDirectoryName(referencingModuleLocation) ?? Directory.GetCurrentDirectory())
            : Path.GetFullPath(specifier);
        return new ResolvedSpecifier(moduleRequest, path, Uri: null, SpecifierType.RelativeOrAbsolute);
    }

    protected override string LoadModuleContents(Engine engine, ResolvedSpecifier resolved) =>
        _written.TryGetValue(resolved.Key, out var source) ? source : File.ReadAllText(resolved.Key);
}
