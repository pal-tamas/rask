#!/bin/sh
# rask.sh — install the `rask` CLI and the dependencies it actually needs.
#
# `dotnet tool install -g Rask.Cli` is one line, but it only works on a box that already has the
# .NET 10 SDK, and it installs the tool and nothing else. The CLI shells out to more than that:
# `rask db` needs dotnet-ef, every browser-wasm build needs the wasm-tools workload, and the SPA
# templates need Node. Today each of those is discovered by failure — a raw MSBuild error for a
# missing workload, an exit 1 from `rask new --template react`. This script front-loads them.
#
# Everything is installed USER-LOCALLY. Nothing here needs sudo, touches a distro package manager,
# or writes outside $HOME. An SDK already on the box is left exactly as it is.
#
# Docker is the deliberate exception: detected and reported, never installed. Auto-installing a
# container runtime on someone's workstation is not a call an installer gets to make, and only
# `rask deploy` needs it.
#
# TRUNCATION SAFETY. Every statement lives inside a function and the last line of this file is
# `main "$@"`. A short read — a connection dropped mid-`curl | sh` — therefore executes nothing.
# This is the shape rustup and get.docker.com use, and the same reasoning as HostBootstrap.cs:397
# ("a truncated curl | sh can execute half a script"), which downloads get.docker.com to a file
# before running it. scripts/tests/install-script.test.sh asserts this property on THIS file.
#
# POSIX sh, not bash — the advertised one-liner pipes into `sh`. So `set -eu` with no `pipefail`
# (not POSIX), no `[[ ]]`, no arrays, no `local`. The same test asserts that too, so it can't rot.
#
# Usage:  curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh
#         curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh -s -- --prerelease
#         sh rask.sh --help
# Exit:   0 ok · 1 something failed · 2 bad arguments (matches the CLI's own error surface)
set -eu

# --- configuration -------------------------------------------------------------------------------
# Every value is overridable from the environment. That is also the seam the tests drive: they point
# the URLs at a local fixture tree and the install dirs at a mktemp, so no test touches a real box.

RASK_INSTALL_DOTNET_CHANNEL="${RASK_INSTALL_DOTNET_CHANNEL:-10.0}"
RASK_INSTALL_DOTNET_MAJOR="${RASK_INSTALL_DOTNET_MAJOR:-10}"
RASK_INSTALL_DOTNET_ROOT="${RASK_INSTALL_DOTNET_ROOT:-${DOTNET_ROOT:-$HOME/.dotnet}}"
RASK_INSTALL_PREFIX="${RASK_INSTALL_PREFIX:-$HOME/.local/share/rask}"
RASK_INSTALL_DOTNET_SCRIPT_URL="${RASK_INSTALL_DOTNET_SCRIPT_URL:-https://dot.net/v1/dotnet-install.sh}"
RASK_INSTALL_NODE_DIST="${RASK_INSTALL_NODE_DIST:-https://nodejs.org/dist}"
RASK_INSTALL_NODE_MIN="${RASK_INSTALL_NODE_MIN:-22.12.0}"
RASK_INSTALL_PACKAGE="${RASK_INSTALL_PACKAGE:-Rask.Cli}"

# Flag state. Declared up front because `set -u` makes an unset read fatal.
RASK_DO_SDK=1
RASK_DO_EF=1
RASK_DO_WASM_TOOLS=1
RASK_DO_NODE=1
RASK_DO_PATH=1
RASK_DRY_RUN=0
RASK_QUIET=0
RASK_PRERELEASE=0
RASK_PIN_VERSION=""
RASK_WARNINGS=""

# --- output --------------------------------------------------------------------------------------
# No colour, matching every other script in this repo: installer output is routinely piped, teed and
# pasted into issues, where escape codes are noise.

rask_say() {
    [ "$RASK_QUIET" = 1 ] && return 0
    printf '%s\n' "$*"
}

rask_step() {
    [ "$RASK_QUIET" = 1 ] && return 0
    printf '==> %s\n' "$*"
}

rask_detail() {
    [ "$RASK_QUIET" = 1 ] && return 0
    printf '    %s\n' "$*"
}

# A dependency we could not install but that only some commands need. Collected and replayed in the
# summary so a warning in step 3 is still visible after step 7 has scrolled past.
rask_warn() {
    printf 'rask.sh: %s\n' "$1" >&2
    RASK_WARNINGS="$RASK_WARNINGS$1
"
    shift
    for _wline in "$@"; do
        printf '         %s\n' "$_wline" >&2
    done
}

rask_die() {
    _code="$1"
    shift
    _first=1
    for _dline in "$@"; do
        if [ "$_first" = 1 ]; then
            printf 'rask.sh: %s\n' "$_dline" >&2
            _first=0
        else
            printf '         %s\n' "$_dline" >&2
        fi
    done
    exit "$_code"
}

# Run a command, or describe it under --dry-run. Every mutating call in this script goes through it,
# which is what makes `--dry-run` a real guarantee rather than a best effort.
rask_run() {
    if [ "$RASK_DRY_RUN" = 1 ]; then
        rask_detail "(dry-run) $*"
        return 0
    fi
    "$@"
}

# rask_run, for a command whose chatter we discard. The redirect lives INSIDE the helper on purpose:
# writing `rask_run cmd >/dev/null 2>&1` at the call site would also swallow the `(dry-run)` line,
# and a --dry-run that under-reports what it would do is worse than no --dry-run at all.
rask_run_quiet() {
    if [ "$RASK_DRY_RUN" = 1 ]; then
        rask_detail "(dry-run) $*"
        return 0
    fi
    "$@" >/dev/null 2>&1
}

# --- pure helpers --------------------------------------------------------------------------------
# No side effects, no I/O beyond stdin/stdout. scripts/tests/install-script.test.sh drives these
# directly by sourcing this file with RASK_INSTALL_LIB_ONLY=1.

# The nth dot-separated field of a version, or 0 when absent or non-numeric.
rask_version_field() {
    _vf_value="$(printf '%s' "${1:-}" | cut -d. -f"${2:-1}")"
    case "$_vf_value" in
        '' | *[!0-9]*) printf '0' ;;
        *) printf '%s' "$_vf_value" ;;
    esac
}

# rask_version_ge <have> <want> — 0 when have >= want. Tolerates a leading `v` and a prerelease or
# build suffix: 10.0.100-preview.3 compares as 10.0.100, which is what the SDK floor means here.
rask_version_ge() {
    _vg_have="${1:-}"
    _vg_want="${2:-}"
    [ -n "$_vg_have" ] || return 1
    _vg_have="${_vg_have#v}"
    _vg_want="${_vg_want#v}"
    _vg_have="${_vg_have%%-*}"
    _vg_want="${_vg_want%%-*}"
    _vg_i=1
    while [ "$_vg_i" -le 3 ]; do
        _vg_h="$(rask_version_field "$_vg_have" "$_vg_i")"
        _vg_w="$(rask_version_field "$_vg_want" "$_vg_i")"
        [ "$_vg_h" -gt "$_vg_w" ] && return 0
        [ "$_vg_h" -lt "$_vg_w" ] && return 1
        _vg_i=$((_vg_i + 1))
    done
    return 0
}

# stdin: the output of `dotnet --list-sdks`. 0 when any listed SDK has a major >= $1.
# Rask.Cli sets RollForward=Major (Rask.Cli.csproj:14), so a newer major is fine — we must NOT
# demand exactly 10, or this script would reinstall an SDK on every .NET 11 box.
#
# The loop reads from a here-doc rather than a pipe on purpose: `... | while read` runs the loop in a
# subshell, where setting _found would be lost and the function would always report "not found".
rask_dotnet_ok() {
    _do_min="${1:-$RASK_INSTALL_DOTNET_MAJOR}"
    _do_found=1
    _do_sdks="$(cat)"
    while IFS= read -r _do_line; do
        [ -n "$_do_line" ] || continue
        _do_version="${_do_line%% *}"
        if [ "$(rask_version_field "${_do_version#v}" 1)" -ge "$_do_min" ]; then
            _do_found=0
        fi
    done <<RASK_SDK_LIST
$_do_sdks
RASK_SDK_LIST
    return "$_do_found"
}

# uname -s / uname -m  ->  the triple nodejs.org names its tarballs with. Non-zero on anything we
# have no build for, so the caller can degrade to a warning instead of downloading a 404.
rask_node_triple() {
    _nt_os="${1:-$(uname -s)}"
    _nt_arch="${2:-$(uname -m)}"
    case "$_nt_os" in
        Darwin) _nt_o="darwin" ;;
        Linux) _nt_o="linux" ;;
        *) return 1 ;;
    esac
    case "$_nt_arch" in
        arm64 | aarch64) _nt_a="arm64" ;;
        x86_64 | amd64) _nt_a="x64" ;;
        armv7l) _nt_a="armv7l" ;;
        ppc64le) _nt_a="ppc64le" ;;
        s390x) _nt_a="s390x" ;;
        *) return 1 ;;
    esac
    printf '%s-%s' "$_nt_o" "$_nt_a"
}

# stdin: https://nodejs.org/dist/index.json. Prints the newest LTS version, no leading `v`.
#
# `tr '{' '\n'` puts one release object per line — the entries have no nested objects, only a
# `"files":[...]` array of strings — which is enough structure to pick a field without a JSON parser.
# The index is sorted newest-first, so the first LTS line wins. `"lts"` is the codename string on an
# LTS release and the bare literal false otherwise, so matching on `"lts":"` selects exactly the LTS
# lines. No pipefail here (it isn't POSIX), so `head` closing the pipe early is harmless.
rask_node_lts_version() {
    tr '{' '\n' |
        grep '"lts":"' |
        head -n 1 |
        sed -n 's/.*"version":"v\([0-9][0-9.]*\)".*/\1/p'
}

# The shell profile a PATH line belongs in. $1 overrides $SHELL and $2 overrides `uname -s`, both so
# the test can drive the matrix without spawning shells.
rask_profile_file() {
    _pf_shell="${1:-${SHELL:-}}"
    _pf_os="${2:-$(uname -s)}"
    case "$_pf_shell" in
        */zsh) printf '%s' "${ZDOTDIR:-$HOME}/.zshrc" ;;
        */fish) printf '%s' "$HOME/.config/fish/config.fish" ;;
        */bash)
            # A macOS Terminal tab is a login shell, which reads .bash_profile and never .bashrc.
            if [ "$_pf_os" = "Darwin" ]; then
                printf '%s' "$HOME/.bash_profile"
            else
                printf '%s' "$HOME/.bashrc"
            fi
            ;;
        *) printf '%s' "$HOME/.profile" ;;
    esac
}

# True when the SDK to use is the user-local one, i.e. it is actually there.
#
# This gates DOTNET_ROOT, which is not optional and not the same thing as PATH. A global tool ships as
# an apphost, and an apphost does NOT search PATH for a runtime — it looks at DOTNET_ROOT, then the
# registered location, then /usr/share/dotnet. Install the SDK anywhere else and `dotnet` works
# perfectly while `rask` dies with "You must install .NET to run this application", which reads like a
# broken install rather than a missing variable.
#
# Gated on the directory existing because the opposite mistake is worse: exporting DOTNET_ROOT to a
# path that is not there overrides the default location and breaks a system SDK that was working.
rask_local_dotnet() {
    [ -x "${1:-$RASK_INSTALL_DOTNET_ROOT}/dotnet" ]
}

# Every `dotnet` call after the SDK step goes through this, by absolute path when we installed one.
#
# Prepending to PATH is not enough. POSIX shells cache command lookups: `dotnet` is resolved once —
# during detection, when the only one on the box is the system's — and that resolution is reused for
# the rest of the script even after PATH has been changed to put ours first. So on a machine with an
# older system SDK, we would install .NET 10 and then hand the tool install to .NET 9, which fails
# with "Settings file 'DotnetToolSettings.xml' was not found in the package" — an error that says
# nothing about the SDK version it was actually run under.
rask_dotnet_bin() {
    if rask_local_dotnet; then
        printf '%s' "$RASK_INSTALL_DOTNET_ROOT/dotnet"
    else
        printf 'dotnet'
    fi
}

rask_dotnet() {
    "$(rask_dotnet_bin)" "$@"
}

# Same reasoning for the tool we just installed: prefer the copy we put there over anything else on
# PATH, so `rask --version` reports what this run produced.
rask_cli() {
    if [ -x "$RASK_INSTALL_DOTNET_ROOT/tools/rask" ]; then
        "$RASK_INSTALL_DOTNET_ROOT/tools/rask" "$@"
    else
        rask "$@"
    fi
}

# The PATH block we manage, in the dialect of the profile we are writing into.
rask_path_block() {
    _pb_profile="${1:-}"
    printf '# >>> rask installer >>>\n'
    case "$_pb_profile" in
        *config.fish)
            if rask_local_dotnet; then
                printf 'set -gx DOTNET_ROOT "%s"\n' "$RASK_INSTALL_DOTNET_ROOT"
            fi
            printf 'set -gx PATH "%s" "%s/tools" $PATH\n' "$RASK_INSTALL_DOTNET_ROOT" "$RASK_INSTALL_DOTNET_ROOT"
            printf 'set -gx PATH "%s/node/bin" $PATH\n' "$RASK_INSTALL_PREFIX"
            ;;
        *)
            if rask_local_dotnet; then
                printf 'export DOTNET_ROOT="%s"\n' "$RASK_INSTALL_DOTNET_ROOT"
            fi
            printf 'export PATH="%s:%s/tools:$PATH"\n' "$RASK_INSTALL_DOTNET_ROOT" "$RASK_INSTALL_DOTNET_ROOT"
            printf 'export PATH="%s/node/bin:$PATH"\n' "$RASK_INSTALL_PREFIX"
            ;;
    esac
    printf '# <<< rask installer <<<\n'
}

# stdin: a profile. stdout: the same profile with any previously written rask block removed. Run
# before appending, so re-running this script rewrites its block instead of stacking another copy.
rask_strip_path_block() {
    awk '
        /^# >>> rask installer >>>$/ { skip = 1; next }
        /^# <<< rask installer <<<$/ { skip = 0; next }
        skip != 1 { print }
    '
}

rask_usage() {
    cat <<'RASK_USAGE'
rask.sh — install the rask CLI and the dependencies it needs.

Usage:
  curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh
  curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh -s -- --prerelease
  sh rask.sh [options]

Options:
  --version <v>    install a specific Rask.Cli version (default: the latest stable)
  --prerelease     install the latest nightly prerelease instead
  --no-sdk         never install the .NET SDK, even when none is found
  --no-ef          skip the dotnet-ef tool (rask db installs it on first use anyway)
  --no-wasm-tools  skip the wasm-tools workload (needed by every browser-wasm build)
  --no-node        skip Node.js (needed by the SPA templates: react, vue, svelte, angular, ...)
  --no-path        never write to a shell profile
  --dry-run        print what would happen and change nothing
  --quiet          print only errors and the final summary
  --help           show this text

Every option also has an environment equivalent, and every install location is overridable —
see docs/installation.md. Windows: use rask.ps1.
RASK_USAGE
}

# Flags -> RASK_DO_* state. An unknown flag exits 2, not 1: the CLI reserves 2 for bad argv, and an
# installer that silently ignores a misspelled --no-node would install the thing you opted out of.
rask_parse_args() {
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --version)
                [ "$#" -ge 2 ] || rask_die 2 "--version needs a value, e.g. --version 0.20.0."
                RASK_PIN_VERSION="$2"
                shift 2
                ;;
            --prerelease)
                RASK_PRERELEASE=1
                shift
                ;;
            --no-sdk)
                RASK_DO_SDK=0
                shift
                ;;
            --no-ef)
                RASK_DO_EF=0
                shift
                ;;
            --no-wasm-tools)
                RASK_DO_WASM_TOOLS=0
                shift
                ;;
            --no-node)
                RASK_DO_NODE=0
                shift
                ;;
            --no-path)
                RASK_DO_PATH=0
                shift
                ;;
            --dry-run)
                RASK_DRY_RUN=1
                shift
                ;;
            --quiet)
                RASK_QUIET=1
                shift
                ;;
            --help)
                rask_usage
                exit 0
                ;;
            *)
                rask_die 2 "unknown option: $1" "Run \`sh rask.sh --help\` for the supported options."
                ;;
        esac
    done
}

# --- fetching ------------------------------------------------------------------------------------

rask_fetch() {
    if [ -n "${RASK_CURL:-}" ]; then
        "$RASK_CURL" -fsSL --retry 3 --retry-delay 1 -o "$2" "$1"
    else
        "$RASK_WGET" -q -O "$2" "$1"
    fi
}

rask_fetch_stdout() {
    if [ -n "${RASK_CURL:-}" ]; then
        "$RASK_CURL" -fsSL --retry 3 --retry-delay 1 "$1"
    else
        "$RASK_WGET" -q -O - "$1"
    fi
}

rask_sha256() {
    if [ -n "${RASK_SHA256:-}" ]; then
        "$RASK_SHA256" "$1" | cut -d' ' -f1
    else
        "$RASK_SHASUM" -a 256 "$1" | cut -d' ' -f1
    fi
}

# --- steps ---------------------------------------------------------------------------------------

step_preflight() {
    case "$(uname -s)" in
        MINGW* | MSYS* | CYGWIN* | Windows_NT)
            rask_die 2 \
                "this is the Unix installer, and you are on Windows." \
                "Use rask.ps1 instead:" \
                "  irm https://pal-tamas.github.io/rask/rask.ps1 | iex"
            ;;
    esac

    RASK_CURL=""
    RASK_WGET=""
    if command -v curl >/dev/null 2>&1; then
        RASK_CURL="$(command -v curl)"
    elif command -v wget >/dev/null 2>&1; then
        RASK_WGET="$(command -v wget)"
    else
        rask_die 1 "neither \`curl\` nor \`wget\` is on your PATH." "One of them is needed to download anything."
    fi

    RASK_SHA256=""
    RASK_SHASUM=""
    if command -v sha256sum >/dev/null 2>&1; then
        RASK_SHA256="$(command -v sha256sum)"
    elif command -v shasum >/dev/null 2>&1; then
        RASK_SHASUM="$(command -v shasum)"
    fi

    command -v tar >/dev/null 2>&1 ||
        rask_die 1 "\`tar\` is not on your PATH." "It is needed to unpack the .NET SDK and Node."
}

step_dotnet() {
    rask_step "Checking for the .NET $RASK_INSTALL_DOTNET_CHANNEL SDK"

    if { rask_local_dotnet || command -v dotnet >/dev/null 2>&1; } &&
        rask_dotnet --list-sdks 2>/dev/null | rask_dotnet_ok; then
        rask_detail "found $(rask_dotnet --version 2>/dev/null || echo "an SDK") — leaving it alone"
        return 0
    fi

    if [ "$RASK_DO_SDK" = 0 ]; then
        rask_warn \
            "no .NET $RASK_INSTALL_DOTNET_CHANNEL SDK found, and --no-sdk was passed." \
            "\`rask\` will not run until one is installed: https://dot.net"
        return 0
    fi

    rask_detail "not found — installing it into $RASK_INSTALL_DOTNET_ROOT (no sudo, nothing outside \$HOME)"

    # Downloaded to a file and then run, never piped into a shell: a truncated download would
    # otherwise execute half of Microsoft's installer. Same reasoning as HostBootstrap.cs:397.
    _dn_script="$(mktemp)"
    trap 'rm -f "$_dn_script"' EXIT INT TERM
    rask_fetch "$RASK_INSTALL_DOTNET_SCRIPT_URL" "$_dn_script"
    [ -s "$_dn_script" ] || rask_die 1 "downloaded an empty dotnet-install.sh from $RASK_INSTALL_DOTNET_SCRIPT_URL."
    head -n 1 "$_dn_script" | grep -q '^#!' ||
        rask_die 1 "$RASK_INSTALL_DOTNET_SCRIPT_URL did not return a shell script." "Refusing to run it."

    # dotnet-install.sh is a BASH script — its shebang says so and it uses bashisms throughout. This
    # script is POSIX sh, but running that one with `sh` only appears to work: on macOS /bin/sh is bash
    # in POSIX mode and forgives it, while on Debian /bin/sh is dash and it dies with
    # "Syntax error: redirection unexpected" from somewhere around line 681 of a file the user never
    # asked for. So hand it to a real bash, and say plainly when there isn't one.
    _dn_bash="$(command -v bash 2>/dev/null || true)"
    [ -n "$_dn_bash" ] ||
        rask_die 1 \
            "installing the .NET SDK needs \`bash\`, and there isn't one on your PATH." \
            "Microsoft's dotnet-install.sh is a bash script; this installer is not." \
            "Install bash, or install the SDK yourself from https://dot.net and re-run with --no-sdk."

    rask_run "$_dn_bash" "$_dn_script" \
        --channel "$RASK_INSTALL_DOTNET_CHANNEL" \
        --install-dir "$RASK_INSTALL_DOTNET_ROOT" \
        --no-path
    rm -f "$_dn_script"
    trap - EXIT INT TERM

    [ "$RASK_DRY_RUN" = 1 ] && return 0

    # dotnet-install.sh unpacks a tarball; it does not install the native libraries the runtime links
    # against. On a slim Debian or Ubuntu image that means no ICU, and the very first `dotnet` call
    # fails with a globalization error that names no package. Catch it here, where we still know the
    # SDK was just installed, rather than letting it surface three steps later as "rask is broken".
    if ! "$RASK_INSTALL_DOTNET_ROOT/dotnet" --version >/dev/null 2>&1; then
        rask_die 1 \
            "the .NET SDK unpacked into $RASK_INSTALL_DOTNET_ROOT, but it cannot run." \
            "That is almost always a missing native dependency — most often ICU:" \
            "  Debian/Ubuntu:  sudo apt-get install -y libicu-dev" \
            "  Fedora/RHEL:    sudo dnf install -y libicu" \
            "  Alpine:         sudo apk add icu-libs" \
            "Full list: https://learn.microsoft.com/dotnet/core/install/linux"
    fi
}

step_rask() {
    rask_step "Installing $RASK_INSTALL_PACKAGE"

    set -- tool install --global "$RASK_INSTALL_PACKAGE"
    [ -n "$RASK_PIN_VERSION" ] && set -- "$@" --version "$RASK_PIN_VERSION"
    [ "$RASK_PRERELEASE" = 1 ] && set -- "$@" --prerelease

    # `dotnet tool install` fails when the tool is already there, which is the common case on a
    # re-run and on an upgrade. Fall back to `update`, so this script is idempotent and doubles as
    # the upgrade path — `dotnet tool update -g Rask.Cli` needs no separate instructions.
    if ! rask_run_quiet "$(rask_dotnet_bin)" "$@"; then
        rask_detail "already installed — updating instead"
        set -- tool update --global "$RASK_INSTALL_PACKAGE"
        [ -n "$RASK_PIN_VERSION" ] && set -- "$@" --version "$RASK_PIN_VERSION"
        [ "$RASK_PRERELEASE" = 1 ] && set -- "$@" --prerelease
        rask_run_quiet "$(rask_dotnet_bin)" "$@" ||
            rask_die 1 \
                "could not install or update $RASK_INSTALL_PACKAGE." \
                "Re-run with the output visible: dotnet tool install --global $RASK_INSTALL_PACKAGE"
    fi
}

step_ef() {
    [ "$RASK_DO_EF" = 1 ] || return 0
    rask_step "Installing dotnet-ef (rask db)"

    if rask_dotnet ef --version >/dev/null 2>&1; then
        rask_detail "already installed"
        return 0
    fi
    if ! rask_run_quiet "$(rask_dotnet_bin)" tool install --global dotnet-ef; then
        # Not fatal: EfToolProbe.cs installs it on first use, so `rask db` still works from a cold
        # start. Front-loading it here only saves that first wait.
        rask_warn \
            "could not install dotnet-ef." \
            "\`rask db\` will install it on first use, or: dotnet tool install -g dotnet-ef"
    fi
}

step_wasm_tools() {
    [ "$RASK_DO_WASM_TOOLS" = 1 ] || return 0
    rask_step "Installing the wasm-tools workload (browser-wasm builds)"

    if rask_dotnet workload list 2>/dev/null | grep -q '^wasm-tools'; then
        rask_detail "already installed"
        return 0
    fi
    if ! rask_run_quiet "$(rask_dotnet_bin)" workload install wasm-tools; then
        # Against the ~/.dotnet SDK this script installs, no elevation is needed. Against a
        # pre-existing system SDK (apt, rpm, the macOS pkg) the workload dir is root-owned and this
        # fails. We print the command rather than running it: an installer that silently sudos is a
        # worse trade than one that tells you what to type.
        rask_warn \
            "could not install the wasm-tools workload." \
            "Your .NET SDK is probably system-wide, so the workload needs elevation:" \
            "  sudo dotnet workload install wasm-tools" \
            "Only browser-wasm builds need it — a server app is unaffected."
    fi
}

step_node() {
    [ "$RASK_DO_NODE" = 1 ] || return 0
    rask_step "Checking for Node.js >= $RASK_INSTALL_NODE_MIN (SPA templates)"

    if command -v node >/dev/null 2>&1 &&
        rask_version_ge "$(node --version 2>/dev/null)" "$RASK_INSTALL_NODE_MIN"; then
        rask_detail "found $(node --version 2>/dev/null) — leaving it alone"
        return 0
    fi

    if [ -z "${RASK_SHA256:-}" ] && [ -z "${RASK_SHASUM:-}" ]; then
        rask_warn \
            "no sha256 tool found, so a Node download could not be verified — skipping it." \
            "Install Node.js 22.12 or newer yourself from https://nodejs.org."
        return 0
    fi

    if ! _nd_triple="$(rask_node_triple)"; then
        rask_warn \
            "no Node.js build for $(uname -s)/$(uname -m) — skipping it." \
            "Only \`rask new --template react|vue|svelte|...\` needs Node."
        return 0
    fi

    _nd_version="$(rask_fetch_stdout "$RASK_INSTALL_NODE_DIST/index.json" | rask_node_lts_version)"
    if [ -z "$_nd_version" ]; then
        rask_warn "could not resolve the current Node LTS from $RASK_INSTALL_NODE_DIST/index.json — skipping it."
        return 0
    fi
    rask_detail "installing Node $_nd_version ($_nd_triple) into $RASK_INSTALL_PREFIX/node"

    _nd_name="node-v$_nd_version-$_nd_triple.tar.gz"
    _nd_tmp="$(mktemp -d)"
    trap 'rm -rf "$_nd_tmp"' EXIT INT TERM

    # .tar.gz rather than the smaller .tar.xz: a slim container image has tar but often no xz.
    rask_fetch "$RASK_INSTALL_NODE_DIST/v$_nd_version/$_nd_name" "$_nd_tmp/$_nd_name"
    rask_fetch "$RASK_INSTALL_NODE_DIST/v$_nd_version/SHASUMS256.txt" "$_nd_tmp/SHASUMS256.txt"

    # Verify against the digest nodejs.org publishes — the same shape TailwindCli.cs:90 uses for the
    # Tailwind binary. An unverified tarball unpacked onto someone's PATH is not something to ship.
    _nd_want="$(awk -v want="$_nd_name" '$2 == want { print $1 }' "$_nd_tmp/SHASUMS256.txt")"
    [ -n "$_nd_want" ] || rask_die 1 "$_nd_name is not listed in SHASUMS256.txt for v$_nd_version."
    _nd_have="$(rask_sha256 "$_nd_tmp/$_nd_name")"
    [ "$_nd_have" = "$_nd_want" ] ||
        rask_die 1 "checksum mismatch for $_nd_name." "expected $_nd_want" "got      $_nd_have"
    rask_detail "sha256 verified"

    rask_run rm -rf "$RASK_INSTALL_PREFIX/node"
    rask_run mkdir -p "$RASK_INSTALL_PREFIX/node"
    rask_run tar -xzf "$_nd_tmp/$_nd_name" -C "$RASK_INSTALL_PREFIX/node" --strip-components=1

    rm -rf "$_nd_tmp"
    trap - EXIT INT TERM
}

step_docker() {
    rask_step "Checking for Docker (rask deploy)"

    if command -v docker >/dev/null 2>&1; then
        rask_detail "found"
        return 0
    fi
    # Detected, never installed. The wording matches DockerProbe.cs:44-45 so the two surfaces say
    # the same thing.
    rask_detail "not found — only \`rask deploy\` and \`rask db backup --remote\` need it"
    case "$(uname -s)" in
        Darwin) rask_detail "  brew install --cask docker" ;;
        *) rask_detail "  curl -fsSL https://get.docker.com | sh   (or https://docs.docker.com/get-docker/)" ;;
    esac
}

step_path() {
    [ "$RASK_DO_PATH" = 1 ] || return 0
    _sp_profile="$(rask_profile_file)"
    rask_step "Putting rask on your PATH ($_sp_profile)"

    if [ "$RASK_DRY_RUN" = 1 ]; then
        rask_detail "(dry-run) would rewrite the rask block in $_sp_profile"
        return 0
    fi

    mkdir -p "$(dirname "$_sp_profile")"
    [ -f "$_sp_profile" ] || : >"$_sp_profile"

    # Strip any block a previous run wrote before appending, so re-running rewrites rather than
    # stacking a second copy.
    _sp_tmp="$(mktemp)"
    rask_strip_path_block <"$_sp_profile" >"$_sp_tmp"
    rask_path_block "$_sp_profile" >>"$_sp_tmp"
    cat "$_sp_tmp" >"$_sp_profile"
    rm -f "$_sp_tmp"
}

step_verify() {
    rask_step "Verifying"

    if [ "$RASK_DRY_RUN" = 1 ]; then
        rask_detail "(dry-run) would run: rask --version && rask doctor"
        return 0
    fi

    { [ -x "$RASK_INSTALL_DOTNET_ROOT/tools/rask" ] || command -v rask >/dev/null 2>&1; } ||
        rask_die 1 \
            "\`rask\` was installed but is not on this shell's PATH." \
            "Expected it at $RASK_INSTALL_DOTNET_ROOT/tools/rask."

    # Deliberately NOT `rask --version 2>/dev/null || echo "(version unavailable)"`. That is what this
    # step said at first, and it turned a genuinely broken install — the tool on disk, unable to find a
    # runtime — into a cheerful "(version unavailable)" followed by "Installed." The one job of a
    # verification step is to fail when the thing does not work, so the real error is surfaced instead.
    if ! _sv_version="$(rask_cli --version 2>&1)"; then
        rask_die 1 \
            "\`rask\` is installed but will not run." \
            "$_sv_version" \
            "If that mentions a missing .NET runtime, the SDK is outside the default location and" \
            "DOTNET_ROOT is not set — open a new terminal, or export it by hand:" \
            "  export DOTNET_ROOT=\"$RASK_INSTALL_DOTNET_ROOT\""
    fi

    rask_detail "rask $_sv_version"

    # The installer closes with the project's own diagnostic rather than a bespoke one, so there is
    # exactly one description of a healthy machine. But `doctor` only exists from the release that
    # added it, and `--version` pins whatever the caller asked for: an older CLI answers
    # "Unknown command 'doctor'" and prints its entire help page, which is a baffling way to end an
    # install that worked. So ask what this CLI actually has before calling it.
    if rask_cli --help 2>/dev/null | grep -q '[[:space:]]doctor[[:space:]]'; then
        rask_say ""
        rask_cli doctor || true
    else
        rask_detail "(this version has no \`doctor\` command — upgrade for the environment report)"
    fi
}

step_summary() {
    rask_say ""
    if [ -n "$RASK_WARNINGS" ]; then
        rask_say "Installed, with warnings above."
    else
        rask_say "Installed."
    fi
    if [ "$RASK_DO_PATH" = 1 ]; then
        rask_say ""
        rask_say "Open a new terminal, or load the new environment into this one:"
        # Must match what step_path wrote, DOTNET_ROOT included. Printing the PATH line alone would
        # hand the user a copy-paste that reproduces exactly the failure DOTNET_ROOT exists to prevent.
        if rask_local_dotnet; then
            rask_say "  export DOTNET_ROOT=\"$RASK_INSTALL_DOTNET_ROOT\""
        fi
        rask_say "  export PATH=\"$RASK_INSTALL_DOTNET_ROOT:$RASK_INSTALL_DOTNET_ROOT/tools:$RASK_INSTALL_PREFIX/node/bin:\$PATH\""
    fi
    rask_say ""
    rask_say "Then:"
    rask_say "  rask new MyApp && cd MyApp && rask dev"
}

main() {
    rask_parse_args "$@"
    step_preflight

    # Everything installed here lands under $HOME and is not on PATH yet in THIS shell. Prepend it
    # up front so the later steps — dotnet-ef, the workload, the verify — can see what step_dotnet
    # just installed without the user opening a new terminal first.
    PATH="$RASK_INSTALL_DOTNET_ROOT:$RASK_INSTALL_DOTNET_ROOT/tools:$RASK_INSTALL_PREFIX/node/bin:$PATH"
    export PATH
    export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-1}"
    export DOTNET_NOLOGO="${DOTNET_NOLOGO:-1}"

    rask_say "Installing the rask CLI"
    rask_say ""

    step_dotnet

    # Only now can we know whether the SDK is the user-local one — step_dotnet may have just created
    # it. Everything after this point runs a global tool's apphost, which needs DOTNET_ROOT to find a
    # runtime outside the default location.
    if rask_local_dotnet; then
        DOTNET_ROOT="$RASK_INSTALL_DOTNET_ROOT"
        export DOTNET_ROOT
    fi

    step_rask
    step_ef
    step_wasm_tools
    step_node
    step_docker
    step_path
    step_verify
    step_summary
}

# The guard exists so scripts/tests/install-script.test.sh can source this file and drive the pure
# helpers. It does not weaken the truncation property: a short read never reaches this line at all.
[ -n "${RASK_INSTALL_LIB_ONLY:-}" ] || main "$@"
