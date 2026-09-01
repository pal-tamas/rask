#!/usr/bin/env bash
# Table test for rask.sh — the public installer served at https://rask.sh/rask.sh.
#
# Two halves, and the second is the point.
#
# The first drives the pure helpers (version compare, platform mapping, profile selection, arg
# parsing) by SOURCING rask.sh with RASK_INSTALL_LIB_ONLY=1. Nothing here downloads or installs.
#
# The second asserts structural properties OF THE SHIPPED FILE that no functional test can reach:
# that its last line is the guarded `main "$@"` (so a truncated `curl | sh` executes nothing), that
# it stays POSIX sh (the one-liner pipes into `sh`, where a bashism is a syntax error on someone
# else's machine and never on ours), that --help and the parser list the same flags, and that the
# install URL is byte-identical everywhere it is documented. Those are exactly the failures that
# would ship green otherwise — the file parses, the tests pass, and the one-liner is broken.
#
# Usage:  scripts/tests/install-script.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

failures=0
checked=0

check() {
    local name="$1" expected="$2" actual="$3"
    checked=$((checked + 1))
    if [ "$actual" = "$expected" ]; then
        printf '  ok   %-52s -> %s\n' "$name" "$actual"
    else
        printf '  FAIL %-52s -> %s (expected %s)\n' "$name" "$actual" "$expected" >&2
        failures=$((failures + 1))
    fi
}

# The seam: the guard on the last line of rask.sh lets us source it without running main.
RASK_INSTALL_LIB_ONLY=1
export RASK_INSTALL_LIB_ONLY
# shellcheck source=../../rask.sh
. "$root/rask.sh"

# --- rask_version_ge -------------------------------------------------------------------------

ge() { if rask_version_ge "$1" "$2"; then printf 'yes'; else printf 'no'; fi; }

echo "==> rask_version_ge"
check "10.0.100 >= 10.0"                       yes "$(ge 10.0.100 10.0)"
check "9.0.400 >= 10.0"                        no  "$(ge 9.0.400 10.0)"
check "11.0.100 >= 10.0"                       yes "$(ge 11.0.100 10.0)"
check "10.0.100-preview.3 >= 10.0.100"         yes "$(ge 10.0.100-preview.3 10.0.100)"
check "10.0 >= 10.0.100"                       no  "$(ge 10.0 10.0.100)"
check "v24.14.0 >= 22.12.0 (node prints a v)"  yes "$(ge v24.14.0 22.12.0)"
check "v22.11.0 >= 22.12.0"                    no  "$(ge v22.11.0 22.12.0)"
check "v22.12.0 >= 22.12.0 (equal)"            yes "$(ge v22.12.0 22.12.0)"
check "empty >= 22.12.0"                       no  "$(ge '' 22.12.0)"
check "garbage >= 22.12.0"                     no  "$(ge 'not-a-version' 22.12.0)"

# --- the SHIPPED Node floor ------------------------------------------------------------------
# Against the real default rather than a literal, so this pins what the installer actually does. The
# floor decides whether an existing Node is LEFT ALONE, and what that Node has to be able to do is
# scaffold — which runs create-vite@latest and @angular/cli@latest. Angular's CLI refuses below
# ^22.22.3 || ^24.15.0 || >=26.0.0, so 24.14.0 is the exact machine that installed cleanly and then
# could not run `rask new --template angular` (#886).
echo "==> the shipped RASK_INSTALL_NODE_MIN"
check "24.14.0 is refused (the #886 machine)"  no  "$(ge v24.14.0 "$RASK_INSTALL_NODE_MIN")"
check "24.15.0 satisfies it"                   yes "$(ge v24.15.0 "$RASK_INSTALL_NODE_MIN")"
check "the current LTS satisfies it"           yes "$(ge v24.20.0 "$RASK_INSTALL_NODE_MIN")"
check "22.12.0 no longer satisfies it"         no  "$(ge v22.12.0 "$RASK_INSTALL_NODE_MIN")"

# --- rask_dotnet_ok --------------------------------------------------------------------------
# Real `dotnet --list-sdks` output: "<version> [<path>]", newest last, one per line.

sdk_ok() { if printf '%s\n' "$1" | rask_dotnet_ok "${2:-10}"; then printf 'yes'; else printf 'no'; fi; }

echo "==> rask_dotnet_ok"
check "10.0.400 present" yes "$(sdk_ok '10.0.400 [/usr/share/dotnet/sdk]')"
check "only 8.0 and 9.0" no "$(sdk_ok '8.0.404 [/usr/share/dotnet/sdk]
9.0.305 [/usr/share/dotnet/sdk]')"
check "9.0 and 10.0 side by side" yes "$(sdk_ok '9.0.305 [/usr/share/dotnet/sdk]
10.0.100 [/usr/share/dotnet/sdk]')"
# RollForward=Major (Rask.Cli.csproj:14) means a newer major runs the tool fine. Demanding exactly
# 10 would reinstall an SDK on every .NET 11 box.
check "11.0 alone satisfies the floor" yes "$(sdk_ok '11.0.100 [/usr/share/dotnet/sdk]')"
check "a preview 10.0 counts" yes "$(sdk_ok '10.0.100-preview.5.25277.114 [/usr/share/dotnet/sdk]')"
check "no SDKs at all" no "$(sdk_ok '')"

# --- rask_node_triple ------------------------------------------------------------------------

triple() { rask_node_triple "$1" "$2" 2>/dev/null || printf 'unsupported'; }

echo "==> rask_node_triple"
check "Darwin/arm64"   darwin-arm64 "$(triple Darwin arm64)"
check "Darwin/x86_64"  darwin-x64   "$(triple Darwin x86_64)"
check "Linux/aarch64"  linux-arm64  "$(triple Linux aarch64)"
check "Linux/x86_64"   linux-x64    "$(triple Linux x86_64)"
check "Linux/armv7l"   linux-armv7l "$(triple Linux armv7l)"
check "FreeBSD/x86_64" unsupported  "$(triple FreeBSD x86_64)"
check "Linux/riscv64"  unsupported  "$(triple Linux riscv64)"

# --- rask_node_lts_version -------------------------------------------------------------------
# Shaped like the real nodejs.org index.json: one line, newest first, "lts" is a codename string on
# an LTS release and the bare literal false otherwise.

cat >"$tmp/index.json" <<'FIXTURE'
[{"version":"v25.1.0","date":"2026-08-01","files":["linux-x64"],"lts":false,"security":false},{"version":"v24.14.0","date":"2026-07-01","files":["linux-x64","darwin-arm64"],"lts":"Krypton","security":false},{"version":"v22.12.0","date":"2024-12-03","files":["linux-x64"],"lts":"Jod","security":false}]
FIXTURE

echo "==> rask_node_lts_version"
check "picks the newest LTS, not the newest release" 24.14.0 "$(rask_node_lts_version <"$tmp/index.json")"
check "no LTS in the index yields nothing" "" \
    "$(printf '%s' '[{"version":"v25.1.0","lts":false}]' | rask_node_lts_version)"

# --- rask_profile_file -----------------------------------------------------------------------

echo "==> rask_profile_file"
check "zsh"              "$HOME/.zshrc"                   "$(rask_profile_file /bin/zsh Linux)"
check "bash on Linux"    "$HOME/.bashrc"                  "$(rask_profile_file /bin/bash Linux)"
# A macOS Terminal tab is a login shell: it reads .bash_profile and never .bashrc.
check "bash on Darwin"   "$HOME/.bash_profile"            "$(rask_profile_file /bin/bash Darwin)"
check "fish"             "$HOME/.config/fish/config.fish" "$(rask_profile_file /usr/bin/fish Linux)"
check "unknown shell"    "$HOME/.profile"                 "$(rask_profile_file /bin/ksh Linux)"
# A cron job, a Docker RUN layer and a bare `sh -c` all reach here with no SHELL at all. Expressed by
# unsetting it rather than by passing '', which the `${1:-${SHELL:-}}` default reads as "not supplied"
# and resolves from the environment — correct for every real caller, which passes nothing.
check "unset SHELL"      "$HOME/.profile"                 "$(unset SHELL && rask_profile_file '' Linux)"

# --- rask_parse_args -------------------------------------------------------------------------
# Each case runs in a subshell so the flag state cannot leak into the next.

state_after() {
    local var="$1"
    shift
    (
        rask_parse_args "$@" >/dev/null 2>&1
        eval "printf '%s' \"\$$var\""
    )
}

code_of() {
    local code=0
    ("$@") >/dev/null 2>&1 || code=$?
    printf '%s' "$code"
}

echo "==> rask_parse_args"
check "default: install the SDK"     1 "$(state_after RASK_DO_SDK)"
check "--no-sdk"                     0 "$(state_after RASK_DO_SDK --no-sdk)"
check "--no-ef"                      0 "$(state_after RASK_DO_EF --no-ef)"
check "--no-wasm-tools"              0 "$(state_after RASK_DO_WASM_TOOLS --no-wasm-tools)"
check "--no-node"                    0 "$(state_after RASK_DO_NODE --no-node)"
check "--no-path"                    0 "$(state_after RASK_DO_PATH --no-path)"
check "--dry-run"                    1 "$(state_after RASK_DRY_RUN --dry-run)"
check "--quiet"                      1 "$(state_after RASK_QUIET --quiet)"
check "--prerelease"                 1 "$(state_after RASK_PRERELEASE --prerelease)"
check "--version takes its value"    0.20.0 "$(state_after RASK_PIN_VERSION --version 0.20.0)"
check "flags combine"                0 "$(state_after RASK_DO_NODE --no-node --dry-run --quiet)"
check "--help exits 0"               0 "$(code_of rask_parse_args --help)"
# Exit 2 for bad argv, matching the CLI. An installer that shrugged off a misspelled --no-node would
# install the very thing you opted out of.
check "unknown flag exits 2"         2 "$(code_of rask_parse_args --no-nod)"
check "--version with no value is 2" 2 "$(code_of rask_parse_args --version)"
check "a bare word exits 2"          2 "$(code_of rask_parse_args install)"

# --- step_path -------------------------------------------------------------------------------
# Drives the REAL step_path, not a re-implementation of it, against a fake HOME. SHELL=/bin/sh so
# the profile resolves the same way on macOS and on Linux.

echo "==> step_path (the shipped function, against a throwaway HOME)"
mkdir -p "$tmp/home"
(
    HOME="$tmp/home"
    SHELL=/bin/sh
    RASK_DRY_RUN=0
    RASK_DO_PATH=1
    printf 'export EDITOR=vim\n' >"$tmp/home/.profile"
    step_path >/dev/null
    step_path >/dev/null
    step_path >/dev/null
)
check "writes exactly one block after three runs" 1 \
    "$(grep -c '^# >>> rask installer >>>$' "$tmp/home/.profile")"
check "leaves the pre-existing profile alone" 1 \
    "$(grep -c '^export EDITOR=vim$' "$tmp/home/.profile")"
check "puts the tools dir on PATH" 1 \
    "$(grep -c 'dotnet/tools' "$tmp/home/.profile")"
check "block is closed" 1 \
    "$(grep -c '^# <<< rask installer <<<$' "$tmp/home/.profile")"

(
    HOME="$tmp/home"
    SHELL=/bin/sh
    RASK_DRY_RUN=1
    RASK_DO_PATH=1
    step_path >/dev/null
)
check "--dry-run leaves the profile untouched" 1 \
    "$(grep -c '^# >>> rask installer >>>$' "$tmp/home/.profile")"

# DOTNET_ROOT, both directions. PATH alone is not enough for a global tool: its apphost does not
# search PATH for a runtime, so an SDK outside the default location leaves `rask` reporting "You must
# install .NET to run this application" while `dotnet` works fine. And the opposite mistake is worse —
# exporting DOTNET_ROOT to a directory that is not there overrides the default location and breaks a
# machine-wide SDK that was working. So it is written when, and only when, the local SDK exists.
(
    HOME="$tmp/local-sdk"
    SHELL=/bin/sh
    RASK_DRY_RUN=0
    RASK_DO_PATH=1
    RASK_INSTALL_DOTNET_ROOT="$tmp/local-sdk/.dotnet"
    mkdir -p "$RASK_INSTALL_DOTNET_ROOT"
    printf '#!/bin/sh\n' >"$RASK_INSTALL_DOTNET_ROOT/dotnet"
    chmod +x "$RASK_INSTALL_DOTNET_ROOT/dotnet"
    step_path >/dev/null
)
check "a user-local SDK gets DOTNET_ROOT" 1 \
    "$(grep -c '^export DOTNET_ROOT=' "$tmp/local-sdk/.profile" || true)"

(
    HOME="$tmp/system-sdk"
    SHELL=/bin/sh
    RASK_DRY_RUN=0
    RASK_DO_PATH=1
    RASK_INSTALL_DOTNET_ROOT="$tmp/system-sdk/.dotnet" # deliberately never created
    mkdir -p "$HOME"
    step_path >/dev/null
)
check "a system-only SDK is left without DOTNET_ROOT" 0 \
    "$(grep -c '^export DOTNET_ROOT=' "$tmp/system-sdk/.profile" || true)"

# --- shape of the shipped file -----------------------------------------------------------------
# Properties of rask.sh itself. These are the ones that would otherwise ship green.

echo "==> rask.sh (shape of the artifact)"

# Truncation safety. A dropped connection mid-`curl | sh` hands sh a prefix of this file; because
# every statement is inside a function and the only top-level call is the very last line, a prefix
# defines some functions and runs nothing. Same reasoning as HostBootstrap.cs:397.
check "last non-blank line invokes main" \
    '[ -n "${RASK_INSTALL_LIB_ONLY:-}" ] || main "$@"' \
    "$(grep -v '^[[:space:]]*$' rask.sh | tail -n 1)"
check "main is invoked exactly once, at column 0" 1 \
    "$(grep -c '^\[ -n "${RASK_INSTALL_LIB_ONLY:-}" \] || main "\$@"$' rask.sh)"

# And the property itself, empirically: feed sh a PREFIX of the file — what a dropped connection
# leaves behind — and nothing may happen. Every prefix is run for real, with $HOME and both install
# roots pointed into a throwaway directory and the package name poisoned, so even a total failure of
# this assertion cannot touch the machine running the suite.
#
# A prefix that lands mid-heredoc is a syntax error, which is a pass: sh printed a complaint and ran
# nothing. What we are looking for is the banner main prints, or a byte written into HOME.
lines="$(wc -l <rask.sh | tr -d ' ')"
ran_main=""
wrote_home=""
for cut in $(seq 10 20 $((lines - 1))) $((lines - 1)); do
    head -n "$cut" rask.sh >"$tmp/prefix.sh"
    rm -rf "$tmp/thome"
    mkdir -p "$tmp/thome"
    out="$(
        HOME="$tmp/thome" \
            RASK_INSTALL_DOTNET_ROOT="$tmp/thome/.dotnet" \
            RASK_INSTALL_PREFIX="$tmp/thome/prefix" \
            RASK_INSTALL_PACKAGE="Rask.Cli.NoSuchPackage.$$" \
            sh "$tmp/prefix.sh" 2>&1 || true
    )"
    case "$out" in
        *'Installing the rask CLI'*) ran_main="line $cut${ran_main:+, $ran_main}" ;;
    esac
    if [ -n "$(ls -A "$tmp/thome" 2>/dev/null)" ]; then
        wrote_home="line $cut${wrote_home:+, $wrote_home}"
    fi
done
check "no prefix of rask.sh reaches main"     "" "$ran_main"
check "no prefix of rask.sh writes to \$HOME" "" "$wrote_home"

# POSIX sh, not bash: the advertised one-liner pipes into `sh`, so a bashism is a syntax error on a
# Debian box and never on a developer's macOS bash. Comments are excluded — this file discusses the
# very constructs it must not use.
code_only() { grep -vE '^[[:space:]]*#' rask.sh; }

# `dash -n` is the authoritative check and catches a here-string or a process substitution outright.
# It does NOT catch `[[ ]]` or `local`, which dash parses happily and then fails on at runtime, or
# accepts as an extension — hence the greps.
check "dash accepts it"     ok "$(dash -n rask.sh >/dev/null 2>&1 && printf ok || printf 'parse-error')"
# `[[` not followed by `:` — so bash's [[ ]] is caught while the POSIX character class `[[:space:]]`,
# which is legitimate in a grep pattern, is not.
check "no [[ ]]"            "" "$(code_only | grep -nE '\[\[([^:]|$)' || true)"
check "no pipefail"         "" "$(code_only | grep -n 'set -o pipefail' || true)"
check "no local"            "" "$(code_only | grep -nE '^[[:space:]]*local ' || true)"
check "no declare"          "" "$(code_only | grep -nE '^[[:space:]]*declare ' || true)"
check "no bash arrays"      "" "$(code_only | grep -nE '=\(' || true)"
check "shebang is /bin/sh"  "#!/bin/sh" "$(head -n 1 rask.sh)"

# --help and the parser must list the same flags. A flag documented but unparsed exits 2 on a user
# who followed the docs; a flag parsed but undocumented is invisible.
help_flags="$(rask_usage | grep -oE '^  --[a-z-]+' | tr -d ' ' | sort)"
parser_flags="$(sed -n '/^rask_parse_args()/,/^}/p' rask.sh | grep -oE '^            --[a-z-]+\)' | tr -d ' )' | sort)"
check "--help and the parser agree on flags" "$help_flags" "$parser_flags"

# --- the install URL, everywhere it is written --------------------------------------------------
# The one-liner appears in the script, the landing page and seven docs. Nothing but this check stops
# them drifting apart, and a wrong URL in the README is a broken front door.

echo "==> install URL consistency"
sh_url="https://rask.sh/rask.sh"
ps1_url="https://rask.sh/rask.ps1"

for f in \
    rask.sh \
    README.md \
    NUGET.md \
    src/Rask.Cli/NUGET.md \
    docs/cli.md \
    docs/getting-started.md \
    docs/installation.md \
    llms.txt \
    samples/Rask.Example.Site/InstallTabs.cs; do
    check "$f carries the canonical rask.sh URL" yes \
        "$(grep -qF "$sh_url" "$f" && printf yes || printf no)"
done

for f in rask.ps1 README.md docs/installation.md; do
    check "$f carries the canonical rask.ps1 URL" yes \
        "$(grep -qF "$ps1_url" "$f" && printf yes || printf no)"
done

# A stale host is the drift that actually happens: rask.dev is a domain this project does not own
# (it exists only as a fake problem-type URI in the Cqrs tests), and a raw.githubusercontent URL
# would pin the installer to a branch rather than the published site.
check "nothing advertises rask.dev" "" \
    "$(grep -rn 'rask\.dev/rask\.\(sh\|ps1\)' README.md NUGET.md docs/ llms.txt rask.sh rask.ps1 2>/dev/null || true)"
check "nothing advertises a raw.githubusercontent installer" "" \
    "$(grep -rn 'raw\.githubusercontent\.com.*rask\.\(sh\|ps1\)' README.md NUGET.md docs/ llms.txt 2>/dev/null || true)"

# The GitHub Pages bundle is what actually serves the URL above. If this copy step is dropped, every
# documented install command 404s while every other test in the repo stays green.
check "pages.yml publishes rask.sh" yes \
    "$(grep -qE 'cp .*rask\.sh' .github/workflows/pages.yml && printf yes || printf no)"
check "pages.yml publishes rask.ps1" yes \
    "$(grep -qE 'cp .*rask\.ps1' .github/workflows/pages.yml && printf yes || printf no)"

# The site moved from a project sub-path (pal-tamas.github.io/rask/) to the apex custom domain
# rask.sh, so it is served from the ORIGIN ROOT. That makes the landing app's RaskPathBase a
# liability rather than a requirement: _RaskRewriteBaseHref would rewrite its <base href> to
# "/rask/" and every asset URL — all document-relative off that base — would 404 on rask.sh.
# Nothing else in the repo reads pages.yml, so this regression ships green.
check "pages.yml roots the landing app at the origin" "" \
    "$(grep -n 'Rask\.Example\.Site.*RaskPathBase' .github/workflows/pages.yml || true)"
check "pages.yml uses no /<repo> sub-path" "" \
    "$(grep -nF 'RaskPathBase=/${{ github.event.repository.name }}' .github/workflows/pages.yml || true)"

# The custom domain, emitted into the bundle so it is reproducible from the tree. GitHub takes the
# domain from the repo's Pages setting, which no diff ever shows; this file is the copy a reviewer
# can actually see, and it has to agree with the host every doc advertises.
check "pages.yml emits the CNAME" yes \
    "$(grep -qE '^ *echo "rask\.sh" > "\$SITE/CNAME"$' .github/workflows/pages.yml && printf yes || printf no)"

# --- gate wiring --------------------------------------------------------------------------------
# rask.sh and rask.ps1 sit at the REPO ROOT, and the pre-commit hook's path filter is a list of
# directory prefixes. Before this file existed, a commit touching only the public installer matched
# none of them and skipped the format + unit gate entirely — which is to say it skipped this test.
# The filter is extracted from the hook and run, rather than grepped for a substring, so this cannot
# pass against a filter that has been edited into something that no longer matches.

echo "==> gate wiring"
precommit_filter="$(sed -n "s/^if ! git diff --cached --name-only | grep -E '\(.*\)' >\/dev\/null; then$/\1/p" .githooks/pre-commit)"
check "the pre-commit path filter is still where we think" yes \
    "$([ -n "$precommit_filter" ] && printf yes || printf no)"

matches_precommit() { printf '%s\n' "$1" | grep -qE "$precommit_filter" && printf yes || printf no; }
check "a rask.sh-only commit runs the unit gate"  yes "$(matches_precommit rask.sh)"
check "a rask.ps1-only commit runs the unit gate" yes "$(matches_precommit rask.ps1)"
check "a scripts/ change still runs it"           yes "$(matches_precommit scripts/tests/install-script.test.sh)"
# Documented rather than fixed: a commit touching ONLY the root README/NUGET.md/llms.txt still skips
# the gate, so the URL checks above would not run for it. Widening the filter makes every typo fix pay
# for the full unit suite, which is a call for the repo owner, not for this test.
check "a root README-only commit still skips it"  no  "$(matches_precommit README.md)"

check "pre-push registers the install gate" yes \
    "$(grep -q 'scripts/run-install-e2e-local.sh' .githooks/pre-push && printf yes || printf no)"
check "the install gate honours its skip flag" yes \
    "$(grep -q 'RASK_SKIP_INSTALL_E2E' scripts/run-install-e2e-local.sh && printf yes || printf no)"

echo
if [ "$failures" -gt 0 ]; then
    echo "install-script.test.sh: $failures of $checked checks FAILED." >&2
    exit 1
fi
echo "install-script.test.sh: $checked checks passed."
