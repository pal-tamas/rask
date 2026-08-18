#!/usr/bin/env bash
# Manual gate: does Litestream restore-verification actually work against a REAL object store?
#
# The unit suite (tests/Rask.SQLite.Litestream.Tests) fakes the replica, so it proves the logic — the
# sentinel round trip, the three-valued outcome, the temp-file cleanup, the absent -if-replica-exists.
# What it cannot prove is that the ARGUMENTS ARE REAL: that `litestream restore -config x.yml -o
# /tmp/verify.db /data/app.db` is a command litestream accepts and that it writes where we claim. That
# only the real binary against a real S3-compatible store can settle, and -config mode is where it
# matters most — it emitted no -o at all before #751, so a verification restore there would have
# overwritten the live database with a copy of itself.
#
# What this proves, in order:
#   A  happy path        — replication running, replica current  -> Verified, live database untouched
#   B  replication lag   — a budget too short to ship a sentinel -> Inconclusive (never Failed)
#   C  THE POINT OF #751 — replica destroyed, replicator alive   -> IsReplicating stays TRUE while
#                                                                   verification reports Failed
#
# C is the whole thesis: "the replicator is running" and "the backup is restorable" are different facts,
# and only one of them is worth an alert.
#
# Requires Docker (pulls minio/minio + minio/mc on first run) and a litestream binary — the one the
# package caches under ~/.rask/litestream/<version>/<rid>/ is found automatically, or set LITESTREAM_BIN.
# Nothing is written inside the repo: the harness project, the databases and the config all live in a
# temp directory that is removed on exit, along with the container.
#
# Not part of run-unit-local.sh: it needs Docker and a network, and it costs ~30s. Run it when you touch
# the verification path or the argument builder.
#
# Usage:  scripts/verify-litestream-minio.sh
# Env:    LITESTREAM_BIN=/path/to/litestream   LSVAL_PORT=9000
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="rask-litestream-verify-minio"
BUCKET="rask"
WORK=""

cleanup() {
  local status=$?
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  [ -n "$WORK" ] && rm -rf "$WORK"
  exit "$status"
}
trap cleanup EXIT

# ---------------------------------------------------------------- preflight

if ! docker info >/dev/null 2>&1; then
  echo "verify-litestream-minio: Docker is not running." >&2
  exit 1
fi

if [ -z "${LITESTREAM_BIN:-}" ]; then
  # The package downloads the binary into a per-user cache at build time; prefer the newest one there,
  # then fall back to PATH.
  LITESTREAM_BIN="$(find "$HOME/.rask/litestream" -type f -name litestream 2>/dev/null | sort | tail -1)"
  [ -z "$LITESTREAM_BIN" ] && LITESTREAM_BIN="$(command -v litestream || true)"
fi

if [ -z "$LITESTREAM_BIN" ] || [ ! -x "$LITESTREAM_BIN" ]; then
  echo "verify-litestream-minio: no litestream binary found. Build a project that references" >&2
  echo "  Rask.SQLite.Litestream (which downloads one), or set LITESTREAM_BIN." >&2
  exit 1
fi

# Never take over a port something else is already serving — shift instead.
PORT="${LSVAL_PORT:-9000}"
if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "verify-litestream-minio: port $PORT is in use, using 9100 instead."
  PORT=9100
  if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "verify-litestream-minio: port $PORT is in use too — set LSVAL_PORT." >&2
    exit 1
  fi
fi

echo "verify-litestream-minio: litestream $("$LITESTREAM_BIN" version), MinIO on port $PORT"

# -P resolves symlinks: on macOS mktemp hands back /var/folders/…, and /var is a symlink to /private/var.
# MSBuild rewrites the harness's absolute ProjectReference into a relative path, and computing that
# against the symlinked form yields a path that does not exist ("project file … was not found").
WORK="$(cd "$(mktemp -d)" && pwd -P)"
mkdir -p "$WORK/harness" "$WORK/verify-temp"

# ---------------------------------------------------------------- MinIO

docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" -p "$PORT:9000" \
  -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin \
  minio/minio server /data >/dev/null

for _ in $(seq 1 30); do
  if curl -fs -o /dev/null "http://localhost:$PORT/minio/health/live"; then break; fi
  sleep 1
done
curl -fs -o /dev/null "http://localhost:$PORT/minio/health/live" || {
  echo "verify-litestream-minio: MinIO never became healthy." >&2
  exit 1
}

# The mc container reaches the published port through the host, not through localhost-in-container.
MC_ENDPOINT="http://host.docker.internal:$PORT"
docker run --rm --entrypoint sh minio/mc -c \
  "mc alias set local $MC_ENDPOINT minioadmin minioadmin >/dev/null && mc mb --ignore-existing local/$BUCKET" >/dev/null

# ---------------------------------------------------------------- config + harness

# litestream 0.5.x config: a single `replica:` per db (0.3.x took a `replicas:` list). force-path-style
# is what makes the AWS SDK address MinIO as bucket-in-path rather than as a subdomain.
cat > "$WORK/litestream.yml" <<YAML
dbs:
  - path: $WORK/app.db
    replica:
      type: s3
      bucket: $BUCKET
      path: db
      endpoint: http://localhost:$PORT
      force-path-style: true
      access-key-id: minioadmin
      secret-access-key: minioadmin
YAML

cat > "$WORK/harness/harness.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <AssemblyName>litestream-validation</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="$REPO_ROOT/src/Rask.SQLite.Litestream/Rask.SQLite.Litestream.csproj"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.11"/>
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.11"/>
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.11"/>
  </ItemGroup>

</Project>
XML

# Quoted heredoc: the C# below is written verbatim, interpolations and all.
cat > "$WORK/harness/Program.cs" <<'CSHARP'
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.SQLite.Litestream;

// Everything here runs the shipping code path: AddRaskSqliteLitestream, the real CliWrap executor, the
// real hosted replication service, the real verifier. Nothing is re-implemented for the test.

var dir = Environment.GetEnvironmentVariable("LSVAL_DIR") ?? throw new InvalidOperationException("LSVAL_DIR");
var bin = Environment.GetEnvironmentVariable("LITESTREAM_BIN") ?? throw new InvalidOperationException("LITESTREAM_BIN");
var bucket = Environment.GetEnvironmentVariable("LSVAL_BUCKET") ?? "rask";
var endpoint = Environment.GetEnvironmentVariable("LSVAL_MC_ENDPOINT") ?? "http://host.docker.internal:9000";

var dbPath = Path.Combine(dir, "app.db");
var configPath = Path.Combine(dir, "litestream.yml");
var tempDirectory = Path.Combine(dir, "verify-temp");

var failures = new List<string>();
void Check(string label, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "  PASS" : "  FAIL")}  {label}: {detail}");
    if (!ok)
    {
        failures.Add(label);
    }
}

await using (var seed = new SqliteConnection($"Data Source={dbPath}"))
{
    await seed.OpenAsync();
    await using var command = seed.CreateCommand();
    command.CommandText = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS orders (id INTEGER PRIMARY KEY, total TEXT NOT NULL);
        INSERT INTO orders (total) VALUES ('12.00'), ('7.50');
        """;
    await command.ExecuteNonQueryAsync();
}

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
services.AddRaskSqliteLitestream(o =>
{
    o.ExecutablePath = bin;
    o.DatabasePath = dbPath;
    // -config mode on purpose: the path that emitted no -o at all before, so a verification restore
    // would have overwritten the live database with a copy of itself.
    o.ConfigPath = configPath;
    o.RestoreOnStartup = false;
    o.Verification.TempDirectory = tempDirectory;
    o.Verification.ReplicationGrace = TimeSpan.FromSeconds(3);
    o.Verification.PollInterval = TimeSpan.FromSeconds(3);
    o.Verification.Timeout = TimeSpan.FromSeconds(60);
});

await using var provider = services.BuildServiceProvider();
var options = provider.GetRequiredService<LitestreamOptions>();
var status = provider.GetRequiredService<LitestreamStatus>();
var verifier = provider.GetRequiredService<ISqliteBackupVerifier>();
var replication = provider.GetServices<IHostedService>().Single();

await replication.StartAsync(CancellationToken.None);
await Task.Delay(TimeSpan.FromSeconds(6));   // let the first snapshot reach the bucket

try
{
    Console.WriteLine();
    Console.WriteLine("[A] happy path — replication running, replica current");
    var a = await verifier.VerifyAsync();
    Check("A.replicating", status.Current.IsReplicating, $"IsReplicating={status.Current.IsReplicating}");
    Check("A.outcome", a.Outcome == LitestreamVerificationOutcome.Verified, $"{a.Outcome} lag={a.ReplicationLag} error={a.LastError}");
    Check("A.live-db-intact", await OrderCount() == 2, $"orders={await OrderCount()}");
    Check("A.temp-cleaned", Directory.GetFileSystemEntries(tempDirectory).Length == 0,
        $"{Directory.GetFileSystemEntries(tempDirectory).Length} entries left behind");

    Console.WriteLine();
    Console.WriteLine("[B] replication lag — a budget too short for the sentinel to have shipped");
    options.Verification.ReplicationGrace = TimeSpan.Zero;
    options.Verification.Timeout = TimeSpan.FromMilliseconds(1);
    var b = await verifier.VerifyAsync();
    Check("B.outcome", b.Outcome == LitestreamVerificationOutcome.Inconclusive, $"{b.Outcome} error={b.LastError}");
    Check("B.keeps-last-verified", b.LastVerifiedAt == a.LastVerifiedAt, $"LastVerifiedAt={b.LastVerifiedAt:O}");
    options.Verification.ReplicationGrace = TimeSpan.FromSeconds(3);
    options.Verification.Timeout = TimeSpan.FromSeconds(30);

    Console.WriteLine();
    Console.WriteLine("[C] the failure #751 is about — replica unreadable, replicator still 'healthy'");
    Docker($"run --rm --entrypoint sh minio/mc -c \"mc alias set local {endpoint} minioadmin minioadmin >/dev/null && mc rb --force local/{bucket}\"");
    await Task.Delay(TimeSpan.FromSeconds(2));
    var c = await verifier.VerifyAsync();
    Check("C.still-replicating", status.Current.IsReplicating, $"IsReplicating={status.Current.IsReplicating}");
    Check("C.outcome", c.Outcome == LitestreamVerificationOutcome.Failed, $"{c.Outcome} error={c.LastError}");
    Check("C.keeps-last-verified", c.LastVerifiedAt == a.LastVerifiedAt, $"LastVerifiedAt={c.LastVerifiedAt:O}");
    Check("C.temp-cleaned", Directory.GetFileSystemEntries(tempDirectory).Length == 0,
        $"{Directory.GetFileSystemEntries(tempDirectory).Length} entries left behind");
}
finally
{
    await replication.StopAsync(CancellationToken.None);
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "ALL CHECKS PASSED" : $"FAILED: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

async Task<long> OrderCount()
{
    await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM orders";
    return (long)(await command.ExecuteScalarAsync())!;
}

static void Docker(string arguments)
{
    using var process = Process.Start(new ProcessStartInfo("docker", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    })!;
    process.WaitForExit();
    Console.WriteLine($"  docker: {process.StandardOutput.ReadToEnd().Trim()}{process.StandardError.ReadToEnd().Trim()}");
}
CSHARP

# ---------------------------------------------------------------- run
#
# No pipe on the run: piping would hand back the exit code of the pipe's last stage and turn a failed
# verification into a green gate.

dotnet build "$WORK/harness/harness.csproj" -v q --nologo

LSVAL_DIR="$WORK" \
LITESTREAM_BIN="$LITESTREAM_BIN" \
LSVAL_BUCKET="$BUCKET" \
LSVAL_MC_ENDPOINT="$MC_ENDPOINT" \
  dotnet run --project "$WORK/harness/harness.csproj" --no-build
