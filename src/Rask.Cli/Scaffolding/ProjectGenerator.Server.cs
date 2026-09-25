using System.Text;

namespace Rask.Cli.Scaffolding;

// The server template: an ASP.NET live-server Rask app.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>server</c> template into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateServer(
        string targetDirectory,
        string name,
        ServerBatteries batteries,
        string version,
        IReadOnlyList<string>? islands = null,
        DotnetTarget? dotnet = null)
    {
        ArgumentNullException.ThrowIfNull(batteries);

        // Apply the flags' implications once, up front, so the template's conditions are evaluated
        // against the resolved set (--jobs means --data means --cqrs, --push means --pwa, …). See
        // ServerBatteries.Normalized. The template tree marks each region with the WEAKEST condition
        // that describes it, which is only correct if implications have already been applied here.
        batteries = batteries.Normalized();

        var files = TemplateMaterializer.Files(
            targetDirectory, "server", name, batteries, version, dotnet ?? DotnetTarget.Default, islands,
            vsCode: VsCodeSetup.Host);

        // The committed Program.cs is the default app — every battery on, one line. A battery turned off is a
        // line in it rather than a missing package, so the file is written here when there is one to say.
        var program = Path.Combine(targetDirectory, "Program.cs");
        if (ServerProgramCs(name, batteries) is { } text)
        {
            files = [.. files.Select(f => string.Equals(f.Path, program, StringComparison.Ordinal) ? f with { Content = text } : f)];
        }

        return new ScaffoldResult(files, ServerNextSteps(name, batteries))
        {
            Packages = ServerPackages(),
        };
    }

    /// <summary>
    ///     The <c>c.X.Off()</c> lines for the batteries this app does without, outermost first: turning the
    ///     database off takes every table-backed battery with it, and turning the PWA off takes push, so
    ///     neither lists what it already implies.
    /// </summary>
    internal static List<string> OffSwitches(ServerBatteries batteries)
    {
        var off = new List<string>();
        if (!batteries.Cqrs)
        {
            off.Add("Cqrs");
        }

        if (!batteries.Data)
        {
            off.Add("Data");
        }
        else
        {
            AddIfOff(batteries.Outbox, "Outbox");
            AddIfOff(batteries.Jobs, "Jobs");
            AddIfOff(batteries.Mail, "Mail");
            AddIfOff(batteries.Cache, "Cache");
            AddIfOff(batteries.Storage, "Storage");
            AddIfOff(batteries.Snapshots, "Snapshots");
            AddIfOff(batteries.Ops, "Ops");
        }

        AddIfOff(batteries.Logs, "Logs");

        if (!batteries.Pwa)
        {
            off.Add("Pwa");
        }
        else
        {
            AddIfOff(batteries.Push, "Push");
        }

        return off;

        void AddIfOff(bool on, string battery)
        {
            if (!on)
            {
                off.Add(battery);
            }
        }
    }

    // Null when every battery is on: the committed one-liner is then the file.
    private static string? ServerProgramCs(string name, ServerBatteries batteries)
    {
        var off = OffSwitches(batteries);
        if (off.Count == 0)
        {
            return null;
        }

        var configure = off.Count == 1
            ? $"app.Configure(c => c.{off[0]}.Off());"
            : "app.Configure(c =>\n{\n" + string.Concat(off.Select(b => $"    c.{b}.Off();\n")) + "});";

        return $$"""
            using {{name}}.Features.Shared;

            var app = RaskApp.Create(args);

            // Every other battery is on, and every setting lives in appsettings.json under "Rask" (docs/configuration.md).
            {{configure}}

            app.Run<App>();

            """;
    }

    // The package list, in the same order the csproj emits them, so `rask new`'s summary matches the file.
    // Rask.Server carries every battery; one turned off is a line in Program.cs, never a missing reference.
    // Rask.DevTools is named directly because its build/ hooks are what keep it out of a Release publish.
    private static List<string> ServerPackages() => ["Rask.Server", "Rask.DevTools"];

    private static string ServerNextSteps(string name, ServerBatteries batteries)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask server app).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (batteries.Docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        AppendBatteryNextSteps(steps, batteries);

        return steps.ToString();
    }

    /// <summary>
    ///     The next-steps paragraphs that depend only on the batteries, not on the template: the first
    ///     entity, and Web Push's keys.
    /// </summary>
    /// <remarks>
    ///     Shared by every template that ships an ASP.NET host with the database batteries —
    ///     <c>server</c> and <c>wasm-hosted</c> — because they are about the batteries, and two copies of
    ///     the same text drift. That is not hypothetical: <c>wasm-hosted</c> was split off with a copy of
    ///     the push paragraph just before that paragraph was fixed on <c>main</c>, and went on printing two
    ///     <c>dotnet user-secrets set</c> lines that fail on every scaffold, because no scaffolded csproj
    ///     carries a <c>UserSecretsId</c>.
    /// </remarks>
    private static void AppendBatteryNextSteps(StringBuilder steps, ServerBatteries batteries)
    {
        // Nothing about whether migrations ran: this text is written before `rask new` restores, builds and
        // migrates, so it cannot know. It used to say "The first migration is already applied to app.db" here,
        // which a failed restore then contradicted two lines later (#1083). NewCommand says it only once the
        // migration has actually succeeded, and prints the manual pair when it was skipped or failed.
        if (batteries.Data)
        {
            steps.Append("\nFor your first entity, declare a class deriving from Aggregate<TId> — no DbSet, no\n");
            steps.Append("configuration class, no registration:\n");
            steps.Append("\n  public sealed class Product : Aggregate<Guid>\n");
            steps.Append("  {\n");
            steps.Append("      public string Name { get; private set; } = \"\";\n");
            steps.Append("  }\n");
            steps.Append("\nThen `rask db add <Name>` and `rask db update` to migrate it into app.db. Read it off\n");
            steps.Append("its generated read face — Product.Read.Where(...) — fill a form with Product.ModelAsync(id),\n");
            steps.Append("and write it off the type — Product.CreateAsync(model),\n");
            steps.Append("Product.UpdateAsync(id, model), Product.DeleteAsync(id).\n");
        }

        if (batteries.Push)
        {
            // It used to say "Web Push needs a VAPID key pair, generate one and save it to user-secrets",
            // and then printed two `dotnet user-secrets set` lines that could not work: no scaffolded
            // csproj carries a UserSecretsId, so both failed with "Could not find the global property
            // 'UserSecretsId'" — the first thing a reader met after `rask new` was an error. The keys are
            // minted here now (WebPushAssembly), so this reports what happened instead of assigning work.
            steps.Append("\nWeb Push needs a VAPID key pair, so one was generated for this app and written to\n");
            steps.Append("appsettings.Development.json. It is gitignored: the private key signs every push you\n");
            steps.Append("send, so it never belongs in the repository. Nothing else to do to push locally.\n");
            steps.Append("\nDeployed, both keys come from the environment — give production a pair of its own:\n");
            steps.Append("  rask deploy --env \"Rask__WebPush__VapidKeys__PublicKey=<public>\" \\\n");
            steps.Append("              --env \"Rask__WebPush__VapidKeys__PrivateKey=<private>\"\n");
            steps.Append("  (VapidKeys.Generate() returns a fresh pair. Replacing a pair unsubscribes\n");
            steps.Append("   everyone already subscribed to the old one.)\n");
        }
    }

    // ---- server-only template files ----

    /// <summary>
    /// A starter catalog. The neutral one carries the app's English; a translation starts as a copy so
    /// the keys line up and the build tells you which ones still need doing (RASK052).
    /// </summary>
    private static string StringsCatalog(bool neutral) =>
        neutral
            ? """
              {
                "AppTitle": "Welcome to Rask",
                "Greeting": "Hello, {name}!",
                "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" }
              }
              """
            : """
              // Translated text for this language. The keys come from the neutral catalog; one that is
              // missing here is a warning (RASK052) and falls back to the neutral text, so a
              // half-finished translation still renders.
              {
                "AppTitle": "Welcome to Rask",
                "Greeting": "Hello, {name}!",
                "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" }
              }
              """;
}
