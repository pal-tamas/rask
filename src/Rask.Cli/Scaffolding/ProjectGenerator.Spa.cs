using System.Text;

namespace Rask.Cli.Scaffolding;

// The TypeScript front-end templates: an ASP.NET host that answers CQRS over JSON and serves the bundle,
// with the framework's own client in client/. The whole tree is committed under src/Rask.Templates/<key>/,
// so the scaffold needs no network and no Node.
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     Generates a TypeScript-front-end app: <c>{name}</c> (ASP.NET + CQRS) with a <c>client</c>
    ///     folder inside it.
    /// </summary>
    /// <remarks>
    ///     ONE project. The client's half of every message is generated TypeScript, so the records live in
    ///     the host and there is nothing for a second .NET project to hold.
    /// </remarks>
    public static ScaffoldResult GenerateSpa(
        string targetDirectory,
        string name,
        SpaFramework framework,
        ServerBatteries requested,
        string version)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(requested);

        // Cqrs first, then Normalized, as wasm-hosted does: the wire IS the template.
        var batteries = (requested with { Cqrs = true }).Normalized();

        var files = TemplateMaterializer.Files(
            targetDirectory, framework.Key, name, batteries, version, vsCode: VsCodeSetup.Host);

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
        return new ScaffoldResult(files, SpaNextSteps(name, framework, batteries))
        {
            Packages = ["Rask.Server", "Rask.Spa.Hosting"],
        };
    }

    private static string SpaNextSteps(string name, SpaFramework framework, ServerBatteries batteries)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask ").Append(framework.DisplayName)
            .Append(" front end + ASP.NET host).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # the host, and the client's dev server on ").Append(framework.DevServerUrl)
            .Append(", together\n");
        if (batteries.Docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        steps.Append("\nThe first build installs the client's dependencies and writes its typed client into\n");
        steps.Append(name).Append("/client/src/rask/ — gitignored, because it is rewritten from the host's message\n");
        steps.Append("records every time they change.\n");

        AppendBatteryNextSteps(steps, batteries);

        return steps.ToString();
    }
}
