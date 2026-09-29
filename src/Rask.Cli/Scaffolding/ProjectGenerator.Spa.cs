using System.Globalization;
using System.Text;

namespace Rask.Cli.Scaffolding;

// The TypeScript front-end templates: an ASP.NET host that answers CQRS over JSON and serves the bundle,
// beside a client the framework's OWN scaffolder produces. Rask overlays four files onto it and patches
// two — everything else is whatever `create-vite` ships today, which is the point.
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     Generates a TypeScript-front-end app: <c>{name}</c> (ASP.NET + CQRS) with a <c>client</c>
    ///     folder inside it, scaffolded by the framework's own tool and then overlaid.
    /// </summary>
    /// <remarks>
    ///     ONE project, not two or three. A C#-on-both-halves solution needs a <c>.Shared</c> because both
    ///     halves are C# and must compile the same record; here the client's half of every contract is
    ///     generated TypeScript, so the messages live in the host and there is nothing for a second .NET
    ///     project to hold — which is why the front end is a folder rather than a sibling, and why the
    ///     host is no longer called <c>.Server</c>: with no sibling, that suffix named nothing.
    /// </remarks>
    public static ScaffoldResult GenerateSpa(
        string targetDirectory,
        string name,
        SpaFramework framework,
        ServerBatteries requested,
        string version,
        DotnetTarget? dotnet = null)
    {
        var batteries = requested.Normalized() with { Cqrs = true };

        // No ExternalScaffolds and no Patches. Both existed only because the client came from
        // `npx create-vite@latest` at scaffold time and was therefore not ours to write: the patches
        // amended somebody else's package.json, .gitignore and index.html in place, precisely because
        // replacing them would have meant carrying a copy of a file we did not own. We own it now —
        // it is committed under src/Rask.Templates/ — so the scaffold needs no network, no Node, and
        // no editing of files it did not write.
        var files = TemplateMaterializer.Files(
            targetDirectory, framework.Key, name, batteries, version, dotnet ?? DotnetTarget.Default,
            vsCode: VsCodeSetup.Host);

        // The same Serve() as wasm-hosted, and the same off-switches: the PWA is the client's own manifest and
        // worker, so a PWA-less app is one without push.
        files = WithProgramCs(
            files,
            targetDirectory,
            ConfiguredProgramCs(
                $"using {name}.Features.Hello;\n\n",
                WasmHostedOffSwitches(batteries),
                "app.Serve();",
                "// The starter's greeting counts visits in memory, so it answers before the app has a table of its own.\n"
                + "app.Services.AddSingleton<VisitCounter>();\n\n"));

        // Rask.Server carries every battery and the host; Rask.Spa.Hosting is named directly because its build
        // steps — the client's install and build, the generated TypeScript — reach only a project that references it.
        return new ScaffoldResult(files, SpaNextSteps(name, framework, batteries.Docker))
        {
            Packages = ["Rask.Server", "Rask.Spa.Hosting"],
            RestoreTarget = $"{name}.slnx",
        };
    }

    private static string SpaNextSteps(string name, SpaFramework framework, bool docker)
    {
        var steps = new StringBuilder();
        steps.AppendLine(CultureInfo.InvariantCulture, $"Next steps for {name} ({framework.DisplayName}):");
        steps.AppendLine();
        steps.AppendLine(CultureInfo.InvariantCulture, $"  cd {name}");
        steps.AppendLine("  rask dev            # the host, and the client's dev server, together");
        steps.AppendLine();
        steps.AppendLine("The first build installs the client's dependencies and writes its generated");
        steps.AppendLine(CultureInfo.InvariantCulture, $"contracts into {name}/client/src/rask/ — that directory is gitignored, because it is");
        steps.AppendLine("rewritten from the server's message records every time they change.");

        if (docker)
        {
            steps.AppendLine();
            steps.AppendLine("  docker build -t " + name.ToLowerInvariant() + " .   # node builds the client, the SDK builds the host");
        }

        return steps.ToString();
    }

}
