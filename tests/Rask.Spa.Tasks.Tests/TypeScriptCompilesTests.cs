using System.Diagnostics;
using Rask.Spa.Tasks;

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
///         Needs a TypeScript compiler, so it is opt-in through <c>RASK_TSC</c>, naming the binary to
///         run — tsgo, the native Go build, which run-unit-local.sh provisions and which needs no node
///         at run time. It does NOT quietly pass when that is unset: an unset variable fails the test
///         with the command to set, because a type-check gate that silently reports success is worse
///         than none. The gate script excludes this test by name when it cannot provision the
///         toolchain, and says so.
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

    [Fact]
    public void The_generated_TypeScript_compiles_against_the_client()
    {
        var tsc = Environment.GetEnvironmentVariable("RASK_TSC");
        Assert.False(
            string.IsNullOrWhiteSpace(tsc),
            "RASK_TSC is not set, so the generated TypeScript was never type-checked. Run this through "
            + "scripts/run-unit-local.sh, which provisions the compiler, or point RASK_TSC at a "
            + "tsgo/tsc binary yourself.");

        Directory.CreateDirectory(_directory);
        var constants = GeneratedTypeScript.Read(TestCompilation.Emit(Contracts, _directory));
        File.WriteAllText(Path.Combine(_directory, "contracts.ts"), constants["Contracts"]);
        File.WriteAllText(Path.Combine(_directory, "messages.ts"), constants["Messages"]);

        foreach (var source in Directory.EnumerateFiles(ClientDirectory(), "*.ts"))
        {
            File.Copy(source, Path.Combine(_directory, Path.GetFileName(source)), overwrite: true);
        }

        File.WriteAllText(Path.Combine(_directory, "usage.check.ts"), Usage);
        LinkPackages(tsc!);

        var (exitCode, output) = Run(
            tsc!,
            "--noEmit --strict --target es2022 --module esnext --moduleResolution bundler "
            + "--lib es2022,dom --skipLibCheck client.ts query.ts contracts.ts messages.ts usage.check.ts");

        Assert.True(exitCode == 0, output);
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

    /// <summary>
    ///     Points the scratch directory at the packages beside the compiler, so <c>query.ts</c> resolves
    ///     TanStack Query for real rather than being excluded from the check for being inconvenient.
    /// </summary>
    private void LinkPackages(string compiler)
    {
        // RASK_TSC names <somewhere>/node_modules/.bin/tsgo, so the package root is two levels up.
        var packages = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(compiler)));
        if (packages is not null && Directory.Exists(packages))
        {
            Directory.CreateSymbolicLink(Path.Combine(_directory, "node_modules"), packages);
        }
    }

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
