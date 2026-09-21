namespace Rask.Providers.E2E.Tests;

/// <summary>The Redis server under test, named by <c>RASK_REDIS_TEST</c>.</summary>
/// <remarks>Like <see cref="Postgres" />: a run with no server reports SKIPPED, never PASSED.</remarks>
internal static class Redis
{
    internal const string SkipReason =
        "Needs a Redis server: run scripts/run-providers-local.sh, or set RASK_REDIS_TEST.";

    internal static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_REDIS_TEST");

    internal static bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    internal static string Required =>
        ConnectionString ?? throw new InvalidOperationException("RASK_REDIS_TEST is not set.");
}
