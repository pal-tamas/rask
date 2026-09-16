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
    ///     Appended to <see cref="ServerPackages" /> rather than listed afresh, so the batteries' packages
    ///     are decided in one place. The order matches the csproj, which emits these last, so
    ///     <c>rask new</c>'s summary reads as the file does.
    /// </remarks>
    private static List<string> WasmHostedPackages(ServerBatteries batteries)
    {
        var packages = ServerPackages(batteries);

        // Serves the browser app in Client/: its build output in Development, its published bundle
        // otherwise.
        packages.Add("Rask.Spa.Hosting");

        // The endpoint half. Its counterpart, Rask.Cqrs.Client, is declared as a browser-only reference
        // so it never reaches this process.
        packages.Add("Rask.Cqrs.Server");

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

        // Same reasoning as the server template's: this text is written before `rask new` restores, builds
        // and migrates, so it cannot claim the first migration ran. NewCommand says that once it has.
        if (batteries.Data)
        {
            steps.Append("\nFor your first entity, declare a class deriving from Aggregate<TId> — no DbSet, no\n");
            steps.Append("configuration class, no registration:\n");
            steps.Append("\n  public sealed class Product : Aggregate<Guid>\n");
            steps.Append("  {\n");
            steps.Append("      public string Name { get; private set; } = \"\";\n");
            steps.Append("  }\n");
            steps.Append("\nThen `rask db add <Name>` and `rask db update` to migrate it into app.db.\n");
        }

        if (batteries.Push)
        {
            steps.Append("\nWeb Push needs a VAPID key pair. Generate one and save it to user-secrets:\n");
            steps.Append("  dotnet user-secrets set \"Rask:WebPush:VapidKeys:PublicKey\" \"<public>\"\n");
            steps.Append("  dotnet user-secrets set \"Rask:WebPush:VapidKeys:PrivateKey\" \"<private>\"\n");
            steps.Append("  (VapidKeys.Generate() prints a pair; the private key must never be served.)\n");
        }

        return steps.ToString();
    }
}
