#!/usr/bin/env bash
# Local install gate — does `rask.sh` actually install anything?
#
# scripts/tests/install-script.test.sh drives the pure helpers and asserts the shape of the file. It
# proves the script PARSES and that its logic is right. It cannot prove that a real `curl | sh` on a
# machine with no .NET, no Node and no tools turns into a working `rask`, because the machine running
# the suite already has all three — which is exactly the machine an installer is never used on.
#
# So this gate runs the working tree's rask.sh inside throwaway containers that genuinely lack what it
# installs, and asserts on what is on the box afterwards: an SDK that runs, a `rask` that answers, a
# dotnet-ef, a Node new enough for the SPA templates, and a scaffolded project that builds.
#
# Requirements: a `docker` CLI and a daemon. Nothing is installed on your machine — every case runs in
# a --rm container, and the only thing mounted is rask.sh itself, read-only.
#
# Slow by construction: each case downloads an SDK. It is path-gated in the pre-push hook, so it only
# runs when the installer itself changes.
#
# Usage:  scripts/run-install-e2e-local.sh
# Skip:   RASK_SKIP_INSTALL_E2E=1 (also honoured by the pre-push hook)
set -euo pipefail

if [ "${RASK_SKIP_INSTALL_E2E:-}" = "1" ]; then
    echo "run-install-e2e-local: RASK_SKIP_INSTALL_E2E=1 — skipping."
    exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

if ! command -v docker >/dev/null 2>&1; then
    echo "run-install-e2e-local: no \`docker\` CLI on PATH — the install gate needs one to boot its bare boxes." >&2
    echo "                       Install a Docker client, or set RASK_SKIP_INSTALL_E2E=1 to bypass." >&2
    exit 1
fi

image="${RASK_INSTALL_E2E_IMAGE:-debian:trixie-slim}"
pwsh_image="${RASK_INSTALL_E2E_PWSH_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}"

failures=0
cases=0

# `curl` and `ca-certificates` are the premise, not the thing under test: someone who cannot fetch a
# URL never reached this script. `libicu` is the .NET runtime's own native dependency — present on
# every desktop and normal server image, absent from -slim. rask.sh detects its absence and says so,
# which case 5 asserts; the other cases install it so they exercise the happy path.
premise='apt-get update -qq >/dev/null && apt-get install -y -qq --no-install-recommends curl ca-certificates libicu76 >/dev/null 2>&1 || apt-get install -y -qq --no-install-recommends curl ca-certificates libicu72 >/dev/null 2>&1'

# Run a script inside a fresh container with rask.sh mounted read-only, and report pass/fail on the
# script's exit status. The container is the assertion: if it exits 0, everything it checked held.
run_case() {
    local name="$1" img="$2" script="$3"
    cases=$((cases + 1))
    echo
    echo "==> [$cases] $name"
    if docker run --rm \
        -v "$root/rask.sh:/rask.sh:ro" \
        -v "$root/rask.ps1:/rask.ps1:ro" \
        -e HOME=/root \
        -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        -e DOTNET_NOLOGO=1 \
        "$img" \
        sh -c "$script"; then
        echo "    PASS: $name"
    else
        echo "    FAIL: $name" >&2
        failures=$((failures + 1))
    fi
}

# --- 1. the whole point: a bare box becomes a working toolchain ---------------------------------

run_case "bare Debian -> a working rask, dotnet-ef and Node" "$image" "
set -eu
$premise

# No .NET and no Node to start with — assert that, so a base image that quietly gains either does not
# turn this case into a no-op that passes for the wrong reason.
! command -v dotnet >/dev/null 2>&1 || { echo 'the base image already has dotnet; this case proves nothing'; exit 1; }
! command -v node   >/dev/null 2>&1 || { echo 'the base image already has node; this case proves nothing';   exit 1; }

sh /rask.sh

# Pick the toolchain up the way a real user does — by starting a shell that reads the profile the
# installer wrote — rather than by reconstructing PATH here. Hand-crafting the environment is how a
# gate ends up proving something the user never gets: PATH alone leaves a global tool unable to find
# a runtime, and only DOTNET_ROOT fixes it.
grep -q 'DOTNET_ROOT' /root/.profile || { echo 'the profile sets no DOTNET_ROOT'; exit 1; }
set +u; . /root/.profile; set -u

# Deliberately version-agnostic. This gate installs whatever Rask.Cli is CURRENTLY PUBLISHED, whose
# command surface lags main by a release or more — asserting on \`rask doctor\` or a \`rask new\` flag
# couples an installer test to what happens to be on nuget.org today, and it went red here for exactly
# that reason. What belongs in this gate is that the installer delivered a working toolchain; whether
# the CLI's own commands behave is scripts/run-cli-build-e2e.sh's job, against the working tree.
rask --version
rask --help >/dev/null
dotnet ef --version
node --version

# The floor the SPA templates need (ProjectGenerator.Spa.cs:128).
node -e 'const [a,b]=process.versions.node.split(\".\").map(Number); if (a<22 || (a===22 && b<12)) { console.error(\"node too old: \"+process.versions.node); process.exit(1); }'

# The PATH block has to be real, not just printed.
grep -q 'rask installer' /root/.profile /root/.bashrc 2>/dev/null || { echo 'no PATH block written'; exit 1; }
"

# --- 2. the toolchain is not just present, it compiles --------------------------------------------
# `rask --version` only proves the apphost found a runtime. This proves the SDK the installer put on
# the box actually builds code — which is what a missing ICU, a half-unpacked tarball or a wrong
# DOTNET_ROOT breaks, and none of those show up in a version string.
#
# Driven through `dotnet` rather than `rask new` on purpose: see the note in case 1.

run_case "the installed SDK actually compiles a project" "$image" "
set -eu
$premise
sh /rask.sh --quiet --no-node
set +u; . /root/.profile; set -u

dotnet new console -o /tmp/probe
dotnet build /tmp/probe --nologo
/tmp/probe/bin/Debug/net*/probe | grep -q 'Hello' || { echo 'the built app did not run'; exit 1; }
"

# --- 3. an existing, older SDK is left alone ----------------------------------------------------
# The promise in docs/installation.md is that a system SDK is detected and never touched. A .NET 9
# box must therefore end up with 9 still where it was and 10 in ~/.dotnet.

run_case "a system .NET 9 is left alone, 10 goes to ~/.dotnet" "mcr.microsoft.com/dotnet/sdk:9.0" "
set -eu
system_dotnet=\$(command -v dotnet)
system_version=\$(dotnet --version)
case \"\$system_version\" in 9.*) ;; *) echo \"expected a 9.x base image, got \$system_version\"; exit 1 ;; esac

sh /rask.sh --no-node --quiet

# The system install is untouched, byte for byte where it was.
test \"\$(command -v dotnet)\" = \"\$system_dotnet\" || { echo 'the system dotnet moved'; exit 1; }
\"\$system_dotnet\" --version | grep -q '^9\.' || { echo 'the system SDK changed version'; exit 1; }

# And a 10 SDK is now available user-locally.
test -x /root/.dotnet/dotnet || { echo 'no user-local SDK installed'; exit 1; }
/root/.dotnet/dotnet --list-sdks | grep -qE '^1[0-9]\.' || { echo 'no 10+ SDK in ~/.dotnet'; exit 1; }
"

# --- 4. --dry-run is exact, not approximate -----------------------------------------------------

run_case "--dry-run writes nothing at all" "$image" "
set -eu
$premise
before=\$(find /root -mindepth 1 | sort)
sh /rask.sh --dry-run
after=\$(find /root -mindepth 1 | sort)
test \"\$before\" = \"\$after\" || {
    echo 'a --dry-run changed the filesystem:'
    printf '%s\n' \"\$before\" > /tmp/b; printf '%s\n' \"\$after\" > /tmp/a; diff /tmp/b /tmp/a || true
    exit 1
}
! command -v rask >/dev/null 2>&1 || { echo 'a --dry-run installed rask'; exit 1; }
"

# --- 5. a missing native dependency is explained, not just failed -------------------------------
# -slim has no ICU, so the SDK unpacks and then cannot run. Without the check in step_dotnet this
# surfaces three steps later as an unexplained failure.

run_case "a missing ICU is named, not left as a raw runtime error" "$image" "
set -eu
apt-get update -qq >/dev/null
apt-get install -y -qq --no-install-recommends curl ca-certificates >/dev/null 2>&1
out=\$(sh /rask.sh 2>&1 || true)
printf '%s\n' \"\$out\" | grep -q 'cannot run' || { echo 'no diagnosis of the unusable SDK'; printf '%s\n' \"\$out\"; exit 1; }
printf '%s\n' \"\$out\" | grep -q 'libicu'    || { echo 'the fix does not name libicu';      printf '%s\n' \"\$out\"; exit 1; }
"

# --- 6. an unknown flag is a usage error, not a silent install ----------------------------------

run_case "an unknown flag exits 2 and installs nothing" "$image" "
set -eu
$premise
code=0
sh /rask.sh --no-nodejs >/dev/null 2>&1 || code=\$?
test \"\$code\" = 2 || { echo \"expected exit 2, got \$code\"; exit 1; }
test ! -d /root/.dotnet || { echo 'a bad flag still installed an SDK'; exit 1; }
"

# --- 7. the Windows twin, as far as a Linux box can take it -------------------------------------
# Nobody working on this repo runs Windows, so rask.ps1 would otherwise ship having never been
# executed at all. PowerShell arrives as a dotnet tool rather than from the powershell image, which
# publishes no arm64 manifest — this way the case runs on an Apple Silicon laptop and on CI alike.
#
# The Windows-only halves are switched off deliberately: USERPROFILE/LOCALAPPDATA do not exist here
# (so the install roots are passed explicitly) and a 'User'-scope PATH write has no meaning on Linux.
# What is left is the control flow, the argument handling and the tool install — the parts most
# likely to be wrong in a file that is never run.

run_case "rask.ps1's control flow and tool install work" "$pwsh_image" "
set -eu
export DOTNET_CLI_HOME=/root
dotnet tool install --global PowerShell >/dev/null
export PATH=\"/root/.dotnet/tools:\$PATH\"

# -NoPath is deliberately NOT passed: a 'User'-scope PATH write is a Windows registry concept, and
# the non-Windows branch that says so is the one place this script can reach it. Skipping the step
# here left a real bug in that branch — a \`-join\` parsed as a parameter to the preceding function —
# invisible to every case.
out=\$(RASK_INSTALL_DOTNET_ROOT=/usr/share/dotnet \
      RASK_INSTALL_PREFIX=/root/.local/share/rask \
      pwsh -NoProfile -File /rask.ps1 -NoSdk -NoNode -NoWasmTools 2>&1)
printf '%s\n' \"\$out\"
printf '%s\n' \"\$out\" | grep -q 'per-user PATH cannot be set outside Windows' || {
    echo 'Step-Path did not report the non-Windows case'; exit 1; }

rask --version
"

echo
if [ "$failures" -gt 0 ]; then
    echo "run-install-e2e-local: $failures of $cases cases FAILED." >&2
    exit 1
fi
echo "==> Install gate passed ($cases cases)."
echo "    Not covered: a real Windows host. Case 7 runs rask.ps1 under PowerShell on Linux with the"
echo "    Windows-only steps switched off, so the SDK install and the user PATH write are unproven —"
echo "    verify those once on a Windows box. Also not covered: macOS, which has no container."
