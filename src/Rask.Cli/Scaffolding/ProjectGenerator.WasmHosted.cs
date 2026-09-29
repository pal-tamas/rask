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

        var files = TemplateMaterializer.Files(
            targetDirectory, "wasm-hosted", name, batteries, version, dotnet ?? DotnetTarget.Default,
            islands,
            // Two debug targets rather than one: the host under the C# debugger, the browser half in
            // the browser's.
            vsCode: VsCodeSetup.WasmHost);

        files = WithProgramCs(files, targetDirectory, ConfiguredProgramCs("", WasmHostedOffSwitches(batteries), "app.Serve();"));

        // The same two the server template names: Rask.Server carries every battery, the endpoint half of remote
        // dispatch and the host that serves Client/'s bundle; Rask.DevTools is named directly because its build/
        // hooks are what keep it out of a Release publish. Client/'s own packages are RaskClientPackageReference
        // items, which never reach this process.
        return new ScaffoldResult(files, WasmHostedNextSteps(name, batteries))
        {
            Packages = ["Rask.Server", "Rask.DevTools"],
        };
    }

    /// <summary>
    ///     <see cref="OffSwitches" />, less the PWA: here it is the browser app's (<c>host.UsePwa</c> in
    ///     <c>Client/Program.cs</c>), and <c>Serve()</c> wires no server-side manifest or worker to turn off. A
    ///     PWA-less app still has no push, so that switch stays.
    /// </summary>
    private static List<string> WasmHostedOffSwitches(ServerBatteries batteries) =>
        [.. OffSwitches(batteries).Select(battery => string.Equals(battery, "Pwa", StringComparison.Ordinal) ? "Push" : battery)];

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
