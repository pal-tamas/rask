using System.Text;

namespace Rask.Cli.Scaffolding;

// The wasm template: a standalone browser-WASM SPA.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>wasm</c> template (a standalone browser-WASM SPA) into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateWasm(string targetDirectory, string name, bool pwa,
        bool docker, string version, ServerBatteries? batteries = null)
    {
        // Both read off the batteries rather than taken as parameters beside them. Styling is one axis
        // with three answers; a bool alongside a ServerBatteries that already carries Styling is two
        // sources for one decision, and the caller that set only one of them is the bug. Localization is
        // the same argument plus a list: until #846 this parameter was accepted and never looked at, so
        // the template took --culture and scaffolded nothing.
        string[] cultures = batteries?.Localization == true ? [.. batteries.Cultures] : [];

        var resolved = (batteries ?? new ServerBatteries()) with { Pwa = pwa, Docker = docker };

        return new ScaffoldResult(
            TemplateMaterializer.Files(targetDirectory, "wasm", name, resolved, version),
            WasmNextSteps(name, docker, cultures.Length > 0))
        {
            Packages = ["Rask.Wasm", "Rask.Ui"],
        };
    }

    /// <summary>The next-steps text printed after a standalone browser-WASM scaffold.</summary>
    private static string WasmNextSteps(string name, bool docker, bool localization)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask browser-WASM SPA).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:80 …\n");
        }

        if (localization)
        {
            // Said here rather than left to be discovered from a bundle report: this is the one battery on
            // this template that costs download rather than only code, and the number is why it is opt-in.
            steps.Append(
                "\nTranslations live in Resources/Strings.<culture>.json and compile to typed members.\n"
                + "Naming a language ships ICU (<RaskGlobalization> in the csproj), which adds roughly a\n"
                + "megabyte to the published bundle — drop the property to get it back. See docs/localization.md.\n");
        }

        return steps.ToString();
    }
}
