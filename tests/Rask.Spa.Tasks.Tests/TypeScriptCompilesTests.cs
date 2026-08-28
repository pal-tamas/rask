using System.Diagnostics;
using Microsoft.Build.Framework;
using Rask.Spa.Tasks;
using Rask.TypeScript.Tasks;

namespace Rask.Spa.Tasks.Tests;

/// <summary>
///     Compiles the generated TypeScript together with the vendored client, under <c>--strict</c>.
/// </summary>
/// <remarks>
///     <para>
///         The one gate that catches the failure mode this whole pipeline is exposed to: the C# side
///         type-checks, the emitter's own tests assert substrings, and the result is still TypeScript
///         that does not compile. A substring assertion cannot tell a well-formed type expression from
///         a malformed one.
///     </para>
///     <para>
///         The compiler is tsgo — the native Go build of TypeScript — fetched as a binary,
///         which caches the download itself, so there is no provisioning step of ours to keep
///         working. <c>RASK_TSC</c> overrides it with a compiler already on disk.
///     </para>
///     <para>
///         The version is PINNED. <c>@typescript/native-preview</c> publishes dated dev builds to
///         <c>latest</c>, and this runs from a pre-commit hook: an unpinned fetch would let somebody
///         else's release turn a commit red for a reason that has nothing to do with the change.
///     </para>
///     <para>
///         It does NOT quietly pass when there is no compiler to run — a type-check gate that
///         silently reports success is worse than none. The gate script excludes this test by name
///         when npx is not on PATH, and says so.
///     </para>
/// </remarks>
public class TypeScriptCompilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "rask-spa-tsc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Every wire shape whose TypeScript could plausibly come out malformed.</summary>
    private const string Contracts = """
        using System;
        using System.Collections.Generic;
        using Rask.Cqrs;

        namespace Shop;

        public enum Status { Draft = 0, Placed = 1 }

        public sealed record Line(string Sku, int Quantity, DateTimeOffset? ShippedAt);

        public sealed record Order(
            Guid Id,
            string? Note,
            Status Status,
            DateTimeOffset PlacedAt,
            DateTime SeenAt,
            DateOnly DeliverBy,
            TimeOnly OpensAt,
            TimeSpan Sla,
            byte[] Signature,
            List<Line> Lines,
            Dictionary<string, Line> ByCode,
            List<DateTimeOffset> Stamps,
            Line? Latest);

        public sealed record GetOrder(Guid Id) : IQuery<Order>;

        public sealed record ListOrders(int Page) : IQuery<IReadOnlyList<Order>>;

        public sealed record PlaceOrder(Guid Id, List<Line> Lines) : ICommand<Order>;

        public sealed record ArchiveOrder(Guid Id) : ICommand;
        """;

    /// <summary>
    ///     The dev build of tsgo this gate runs. Dated, and deliberately not <c>latest</c>.
    /// </summary>
    private const string CompilerVersion = "7.0.0-dev.20260707.2";

    [Fact]
    public void The_generated_TypeScript_compiles_against_the_client()
    {
        var (command, prefix) = Compiler();

        Directory.CreateDirectory(_directory);
        var constants = GeneratedTypeScript.Read(TestCompilation.Emit(Contracts, _directory));
        File.WriteAllText(Path.Combine(_directory, "contracts.ts"), constants["Contracts"]);
        File.WriteAllText(Path.Combine(_directory, "messages.ts"), constants["Messages"]);
        File.Copy(
            Path.Combine(ClientDirectory(), "client.ts"),
            Path.Combine(_directory, "client.ts"),
            overwrite: true);
        File.WriteAllText(Path.Combine(_directory, "usage.check.ts"), Usage);

        // query.ts is NOT in this set. It imports @tanstack/react-query, whose types would have to be
        // installed into the scratch directory, and it is the one client file with nothing generated
        // about it. The scaffolded template's own `npm run build` type-checks it, against the version
        // that template actually pins — which is a better check than a version chosen here.
        var (exitCode, output) = Run(
            command,
            prefix + "--noEmit --strict --target es2022 --module esnext --moduleResolution bundler "
            + "--lib es2022,dom --skipLibCheck client.ts contracts.ts messages.ts usage.check.ts");

        Assert.True(exitCode == 0, output);
    }

    /// <summary>The compiler to run, and anything that has to precede its own arguments.</summary>
    /// <remarks>
    ///     <para>
    ///         Resolved the way the framework's build resolves it: the tarball the registry publishes,
    ///         verified against its own checksum and cached per user. Not through <c>npx</c>, which is
    ///         what this used before Rask had a resolver of its own.
    ///     </para>
    ///     <para>
    ///         The difference is whether the gate can be skipped. With <c>npx</c> the check depended on
    ///         Node.js being installed, on a machine where nothing else about this repository does — and
    ///         a gate whose first question is "is the tooling here?" is a gate that eventually answers
    ///         no and stops running. This one fetches what it needs.
    ///     </para>
    /// </remarks>
    private static (string Command, string Prefix) Compiler()
    {
        if (Environment.GetEnvironmentVariable("RASK_TSC") is { Length: > 0 } configured &&
            !configured.Equals("npx", StringComparison.Ordinal))
        {
            return (configured, string.Empty);
        }

        var engine = new SilentBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = "tsgo",
            Version = CompilerVersion,
        };

        Assert.True(
            task.Execute(),
            "tsgo could not be resolved, so the generated TypeScript was never type-checked: "
            + string.Join("; ", engine.Errors)
            + ". Do not silence this — a type-check gate that reports success without running a "
            + "type-checker is worse than none.");

        return (task.ToolPath, string.Empty);
    }

    /// <summary>Swallows the task's logging; a failure is reported through the assertion above.</summary>
    private sealed class SilentBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? "(no message)");

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(
            string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;
    }

    /// <summary>
    ///     What a call site is supposed to be able to write, asserted at the type level.
    /// </summary>
    private const string Usage = """
        import { rask } from './client'
        import { getOrder, listOrders, placeOrder, archiveOrder } from './messages'

        export async function usage(): Promise<void> {
          // An instant is a Date, so the Date API is available with no cast and no parse.
          const order = await rask.dispatch(getOrder({ id: 'x' }))
          const year: number = order.placedAt.getFullYear()
          void year

          // A calendar date is NOT a Date — this is the assertion that keeps the off-by-one-day bug
          // out, because a Date here would render as the previous day west of UTC.
          // @ts-expect-error DeliverBy is a calendar date, deliberately a string
          const wrong: Date = order.deliverBy
          void wrong

          const sla: string = order.sla
          const opens: string = order.opensAt
          void sla
          void opens

          // Through a list, and through a dictionary.
          const shipped: Date | null = order.lines[0].shippedAt
          const byCode: Date | null = order.byCode['abc'].shippedAt
          void shipped
          void byCode

          const orders = await rask.dispatch(listOrders({ page: 1 }))
          const first: Date = orders[0].placedAt
          void first

          const placed = await rask.dispatch(placeOrder({ id: 'x', lines: [] }))
          void placed.placedAt

          // A void command answers nothing.
          const nothing: void = await rask.dispatch(archiveOrder({ id: 'x' }))
          void nothing

          // @ts-expect-error the payload must match the message
          await rask.dispatch(getOrder({ nope: 1 }))
        }
        """;

    private static string ClientDirectory()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src", "Rask.Spa.Hosting")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!, "src", "Rask.Spa.Hosting", "client");
    }

    private (int ExitCode, string Output) Run(string command, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(command, arguments)
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
