#!/usr/bin/env bash
# Local deploy gate — does `rask deploy` actually deploy?
#
# Every other deploy test in the repo is mocked: a fake process runner records the argv and returns a
# scripted exit code, so they prove the command line Rask *builds* and nothing about whether it *works*.
# This gate points the real `rask deploy` at a throwaway container standing in for a bare VPS (sshd + its
# own Docker daemon, privileged) and asserts on what happened ON THE HOST: an image that built, a
# container that answers HTTP, a Caddyfile a real Caddy accepted, a volume that outlived its container.
#
# Requirements: a `docker` CLI and a daemon that can run a privileged container (Docker Desktop, Colima,
# or podman). Nothing is installed on your machine and your ~/.ssh is never read or written — the gate
# generates a throwaway key and reaches the container through its own ssh config.
#
# Usage:  scripts/run-deploy-e2e-local.sh
# Skip:   RASK_SKIP_DEPLOY_E2E=1 (also honoured by the pre-push hook)
set -euo pipefail

if [ "${RASK_SKIP_DEPLOY_E2E:-}" = "1" ]; then
  echo "run-deploy-e2e-local: RASK_SKIP_DEPLOY_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

if ! command -v docker >/dev/null 2>&1; then
  echo "run-deploy-e2e-local: no \`docker\` CLI on PATH — the deploy gate needs one to boot its fake VPS." >&2
  echo "                      Install a Docker client, or set RASK_SKIP_DEPLOY_E2E=1 to bypass." >&2
  exit 1
fi

# Opted into by the tests themselves; when unset every case reports SKIPPED rather than passing silently.
export RASK_DEPLOY_E2E=1

echo "==> Build the CLI test project (Release)"
dotnet build tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj -c Release -m:1

echo "==> Deploy gate (real rask deploy against a container host)"
dotnet test tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~DeployHostE2ETests" \
  --logger "console;verbosity=normal"

echo
echo "==> Deploy gate passed."
echo "    Not covered: real DNS + Let's Encrypt issuance. The gate uses a .test domain, so Caddy's"
echo "    Caddyfile is validated but ACME never runs — verify that once against a throwaway VPS."
