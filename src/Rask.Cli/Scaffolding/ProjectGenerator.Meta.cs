using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

// The meta framework templates: an ASP.NET host that answers CQRS over JSON and supervises the
// framework's own Node server, beside a front end that framework's OWN creator produces. Rask overlays
// one config file onto it and patches two — everything else is whatever `nuxi` or `create-next-app`
// ships today, which is the point.
//
// The difference from the SPA lane is the whole reason this is a separate generator: there, Rask serves
// a static bundle and node is gone after the build. Here the framework keeps its own server, so what
// has to be arranged is a SECOND process rather than a directory of files — its adapter must emit a
// node server, and its dev server must proxy /_rask back to the host.
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     Generates a meta framework app: <c>{name}</c> (ASP.NET + CQRS, supervising node) with a
    ///     <c>Client</c> folder inside it, scaffolded by the framework's own tool and then overlaid.
    /// </summary>
    /// <remarks>
    ///     One project, and a <c>Client</c> FOLDER rather than a sibling project — the same shape the SPA
    ///     lane settled on in #970, and for a stronger reason here: a meta framework app has no separate
    ///     client artifact for a host to reference at all. It has a server of its own.
    /// </remarks>
    public static ScaffoldResult GenerateMeta(
        string targetDirectory,
        string name,
        MetaTemplate framework,
        ServerBatteries requested,
        string version)
    {
        var batteries = requested.Normalized() with { Cqrs = true };

        // As on the SPA lane: no creator is run and nothing is patched, because the front end is
        // committed under src/Rask.Templates/ rather than fetched from `nuxi@latest` and friends at
        // scaffold time. What that trades away is stated plainly — the tree is a snapshot of what the
        // creator wrote on the day it was imported, and scripts/refresh-templates.sh is how a newer one
        // gets in. What it buys is a scaffold that needs no network, produces the same app twice
        // running, and whose every front-end dependency is a committed manifest this repository can
        // review and Dependabot can bump.
        return new ScaffoldResult(
            VsCodeAssembly.Apply(
                targetDirectory, name,
                TemplateMaterializer.Files(targetDirectory, framework.Key, name, batteries, version)),
            MetaNextSteps(name, framework, batteries.Docker))
        {
            Packages = ["Rask.Cqrs", "Rask.Cqrs.Server", "Rask.Meta.Hosting"],
            RestoreTarget = $"{name}.slnx",
        };
    }

    private static string MetaNextSteps(string name, MetaTemplate framework, bool docker)
    {
        var steps = new StringBuilder();
        steps.AppendLine($"Next steps for {name} ({framework.DisplayName}):");
        steps.AppendLine();
        steps.AppendLine($"  cd {name}");
        steps.AppendLine("  rask dev            # the host, and the framework's own dev server, together");
        steps.AppendLine();
        steps.AppendLine($"The browser talks to {framework.DisplayName} on {framework.DevServerUrl}, which");
        steps.AppendLine("proxies /_rask back to the host — so hot module replacement is native.");
        steps.AppendLine();
        steps.AppendLine($"The first build installs the front end's dependencies and writes your C# contracts");
        steps.AppendLine($"and Rask's browser layer into {name}/{framework.AppDir}/{framework.GeneratedDir}/ — gitignored,");
        steps.AppendLine("because it is rewritten from the message records every time they change.");

        if (docker)
        {
            steps.AppendLine();
            steps.AppendLine($"  docker build -t {name.ToLowerInvariant()} .");
            steps.AppendLine("The image carries a node runtime, which this lane needs and the TypeScript-SPA");
            steps.AppendLine("template does not: the front end has a server of its own, supervised on loopback.");
        }

        return steps.ToString();
    }
}
