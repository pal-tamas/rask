using System.Text.RegularExpressions;
using BenchmarkDotNet.Running;
using Testcontainers.PostgreSql;

namespace Rask.Benchmarks;

/// <summary>
///     <c>postgres-bulk-insert [BenchmarkDotNet args]</c>: starts PostgreSQL on Testcontainers with <c>tc netem</c>
///     delaying its egress, points <see cref="PostgresBulkInsertArms" /> at it, runs the benchmarks and stops it.
/// </summary>
/// <remarks>
///     A server on the same machine answers in microseconds, which hides the one cost that decides a server's write
///     shape: round trips (#1063). <c>RASK_PG_NETEM_DELAY</c> sets the delay (default <c>1ms</c>, a same-region server).
///     Like the provider suite's servers it starts only in CI or where <c>RASK_PROVIDERS_LOCAL=1</c> says Docker may
///     be used, and never against a hand-started server.
/// </remarks>
internal static partial class PostgresBulkInsertReport
{
    internal const string ConnectionVariable = "RASK_PG_BENCH_DB";

    public static async Task<int> Run(string[] args)
    {
        if (!DockerAllowed)
        {
            await Console.Error.WriteLineAsync(
                "postgres-bulk-insert starts PostgreSQL on Testcontainers: it runs in CI, or here with RASK_PROVIDERS_LOCAL=1 (needs Docker).");
            return 2;
        }

        var delay = Environment.GetEnvironmentVariable("RASK_PG_NETEM_DELAY") is { Length: > 0 } given ? given : "1ms";
        if (!Delay().IsMatch(delay))
        {
            await Console.Error.WriteLineAsync($"RASK_PG_NETEM_DELAY={delay} is not a tc delay such as 1ms, 500us or 0.5s.");
            return 2;
        }

        await using var server = new PostgreSqlBuilder("postgres:19-alpine")
            .WithDatabase("rask")
            // tc needs it to add a qdisc to the container's own interface.
            .WithCreateParameterModifier(p => (p.HostConfig ??= new()).CapAdd = ["NET_ADMIN"])
            .Build();
        await server.StartAsync();

        var netem = await server.ExecAsync(
            ["sh", "-c", $"apk add --no-cache iproute2 >/dev/null && tc qdisc add dev eth0 root netem delay {delay}"]);
        if (netem.ExitCode != 0)
        {
            await Console.Error.WriteLineAsync($"tc netem failed ({netem.ExitCode}): {netem.Stderr}");
            return 1;
        }

        Console.WriteLine($"==> PostgreSQL on Testcontainers, {delay} of egress delay");
        // BenchmarkDotNet runs each benchmark in a child process, which inherits this environment.
        Environment.SetEnvironmentVariable(ConnectionVariable, server.GetConnectionString());
        BenchmarkSwitcher.FromAssembly(typeof(PostgresBulkInsertReport).Assembly).Run(args[1..]);
        return 0;
    }

    private static bool DockerAllowed =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
        || Environment.GetEnvironmentVariable("RASK_PROVIDERS_LOCAL") == "1";

    // The delay is spliced into a shell command, so only tc's own time syntax gets through.
    [GeneratedRegex(@"\A[0-9]+(\.[0-9]+)?(us|ms|s)\z")]
    private static partial Regex Delay();
}
