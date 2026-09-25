using System.Text;

namespace Rask.Cli.Scaffolding;

// The wasm-hosted template: a Rask WebAssembly front end on an ASP.NET host — the same lane as the
// TypeScript front-end templates, with C# on both sides of the wire.
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     Generates a hosted-WebAssembly app: <c>{name}</c> (ASP.NET + CQRS endpoints) with a
    ///     <c>Client</c> folder inside it holding the browser half.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ONE project, like every other template. The browser half's project is generated into
    ///         <c>obj/</c> from <c>Client/</c> — <c>Client/Program.cs</c> is what switches that on — so the
    ///         packages only the browser needs are declared as <c>RaskClientPackageReference</c> and never
    ///         reach the server process. That is what replaced the old <c>wasm-hosted</c> template's
    ///         hand-written Client/Server/Shared trio, and it is why there is no <c>.Shared</c> here: the
    ///         messages compile into both halves from the one project.
    ///     </para>
    ///     <para>
    ///         <c>Client</c> keeps its capital. The lowercase <c>client/</c> is the JavaScript lanes'
    ///         convention (and on the meta lane a requirement — several of those scaffolders derive an npm
    ///         package name from the directory and reject capitals); this one is C# and follows .NET's.
    ///     </para>
    ///     <para>
    ///         CQRS is forced on for the same reason <see cref="GenerateSpa" /> forces it: the wire between
    ///         the halves IS the template. <c>Rask.Cqrs.Client</c> goes to the browser and
    ///         <c>Rask.Cqrs.Server</c> to the host — the split exists precisely so the process that answers
    ///         the endpoints never carries the code that calls them. <c>NewCommand</c> refuses
    ///         <c>--no-cqrs</c> here rather than accepting it and turning it back on silently.
    ///     </para>
    /// </remarks>
    public static ScaffoldResult GenerateWasmHosted(
        string targetDirectory,
        string name,
        ServerBatteries requested,
        string version,
        IReadOnlyList<string>? islands = null,
        DotnetTarget? dotnet = null)
    {
        ArgumentNullException.ThrowIfNull(requested);

        // Cqrs first, then Normalized: the implications (--jobs means --data means --cqrs, --push means
        // --pwa) have to be applied to the set the template's conditions are actually evaluated against.
        var batteries = (requested with { Cqrs = true }).Normalized();

        return new ScaffoldResult(
            TemplateMaterializer.Files(
                targetDirectory, "wasm-hosted", name, batteries, version, dotnet ?? DotnetTarget.Default,
                islands,
                // Two debug targets rather than one: the host under the C# debugger, the browser half in
                // the browser's.
                vsCode: VsCodeSetup.WasmHost),
            WasmHostedNextSteps(name, batteries))
        {
            Packages = WasmHostedPackages(batteries),
        };
    }

    /// <summary>The host's package list — the server's, plus the two this lane adds.</summary>
    /// <remarks>
    ///     Appended to <see cref="BatteryPackages" /> rather than listed afresh, so the batteries' packages
    ///     are decided in one place. The order matches the csproj, which emits these last, so
    ///     <c>rask new</c>'s summary reads as the file does.
    /// </remarks>
    private static List<string> WasmHostedPackages(ServerBatteries batteries)
    {
        var packages = BatteryPackages(batteries);

        // Serves the browser app in Client/: its build output in Development, its published bundle
        // otherwise.
        packages.Add("Rask.Spa.Hosting");

        // The endpoint half. Its counterpart, Rask.Cqrs.Client, is declared as a browser-only reference
        // so it never reaches this process.
        packages.Add("Rask.Cqrs.Server");

        return packages;
    }

    // The wasm-hosted host's package list, one per battery, in the order its csproj emits them.
    private static List<string> BatteryPackages(ServerBatteries batteries)
    {
        // No Rask.Tailwind here: the Tailwind build ships INSIDE Rask.Server (RaskTailwindBuildPack),
        // so a scaffolded csproj naming it would be a second copy of the same targets, imported twice.
        //
        // Rask.Ui IS named, and directly rather than through the meta-package, because a package's
        // build/ hooks are imported for a DIRECT reference only — and those hooks are what put daisyUI's
        // plugin next to Styles/app.css and the kit's sheet in wwwroot. It also brings the ~110 Ui*
        // components, which is a bonus here rather than the reason: the starter page writes daisyUI's
        // own class names, so it needs the plugin whether or not it ever names a component.
        //
        // Rask.DevTools is named directly for the same build/-hooks reason: its targets are what keep the
        // devtools out of a Release publish, and an app that does not reference the `Rask` meta-package —
        // which is every scaffolded one — would otherwise never get them.
        var packages = new List<string> { "Rask.Server", "Rask.Ui", "Rask.DevTools" };

        if (batteries.Cqrs)
        {
            packages.Add("Rask.Cqrs");

            // Not a flag of its own. A dispatcher without a cache means every render refetches, and the
            // first thing anyone building a page over IDispatcher needs is the thing that stops that —
            // so it arrives wired rather than as something to discover in the docs later.
            packages.Add("Rask.Query");
        }

        if (batteries.Data)
        {
            packages.Add("Rask.Data");
            packages.Add("Rask.SQLite.EntityFrameworkCore");

            // Continuous backup. Referenced whenever there's a database: the wiring in Program.cs stays
            // inert until Rask:Litestream:ReplicaUrl is set, so this costs an unused reference and buys a
            // one-env-var path from "single copy on one disk" to "the box is disposable".
            packages.Add("Rask.SQLite.Litestream");

            // Accounts. Paired with the database rather than with a flag, because AppDbContextCs maps
            // the account tables whenever there is a context — the two have to move together or the
            // generated `using Rask.Auth;` does not compile.
            packages.Add("Rask.Auth");
        }

        if (batteries.Outbox)
        {
            packages.Add("Rask.Outbox");
        }

        if (batteries.Jobs)
        {
            packages.Add("Rask.Jobs");
        }

        if (batteries.Mail)
        {
            packages.Add("Rask.Mail");
        }

        if (batteries.Cache)
        {
            packages.Add("Rask.Cache");
        }

        if (batteries.Storage)
        {
            packages.Add("Rask.Storage");
        }

        if (batteries.AnySqliteOps)
        {
            packages.Add("Rask.SQLite.Snapshots");
        }

        if (batteries.Logs)
        {
            packages.Add("Rask.Logging");
        }

        if (batteries.Push)
        {
            packages.Add("Rask.WebPush");
        }

        if (batteries.Ops)
        {
            packages.Add("Rask.Dashboard");
        }

        return packages;
    }

    private static string WasmHostedNextSteps(string name, ServerBatteries batteries)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask WebAssembly front end + ASP.NET host).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # the host, and the browser half, together\n");
        if (batteries.Docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        steps.Append("\nPages live in Client/Pages/ and run in the browser. The host answers their queries\n");
        steps.Append("and commands over CQRS — a handler in Features/ is reached from the browser through\n");
        steps.Append("IDispatcher, with the same message record compiled into both halves.\n");

        AppendBatteryNextSteps(steps, batteries);

        return steps.ToString();
    }
}
