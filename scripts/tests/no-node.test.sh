#!/usr/bin/env bash
# gate-inputs: .
# Rask needs no Node: not to build, test or run an app, not to maintain the repo, not in CI. The compilers a build
# runs (tsgo, esbuild, Tailwind) are native binaries Rask downloads by digest, the followers are Python or .NET, and
# the browser is driven by Microsoft.Playwright (.NET). This holds the tree there:
#
#   - no tracked npm manifest, lockfile, Node pin or Node-only module (package.json, package-lock.json, yarn.lock,
#     pnpm-lock.yaml, .npmrc, .node-version, .nvmrc, *.mjs, *.cjs), except the integrity pins named below;
#   - nothing in CI (.github), the scripts, the installers, a Dockerfile or an MSBuild file (*.targets, *.props,
#     *.csproj) runs node, npm, npx, yarn or pnpm, sets Node up (actions/setup-node) or installs it.
#
# Comments are not read: a line that only says why Node is gone is not a use of it. A Node a dependency carries
# inside itself (Microsoft.Playwright's driver) is that package's business, never invoked from here.
#
# Usage:  scripts/tests/no-node.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

echo "==> no node"
git ls-files -z | python3 -I -c '
import re, sys
from pathlib import Path

# Rask.Ui engines bundled by its kit-bundle follower: the lockfile is read for each package tarball URL and its
# sha512 integrity, never installed by npm. The manifest beside it is what the follower bumps.
PINS = {f"src/Rask.Ui/Resources/{engine}/{name}" for engine in ("editor", "phone") for name in ("package.json", "package-lock.json")}
MANIFESTS = {"package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", ".npmrc", ".node-version", ".nvmrc"}
THIS = "scripts/tests/no-node.test.sh"

USES = [re.compile(p) for p in [
    r"\bactions/setup-node\b",
    r"(?<![-.\w])npx\s+\S",
    r"(?<![-.\w])(npm|pnpm|yarn)\s+(install|ci|run|init|exec|i|add|view|pack|publish|test|start|dlx|config|--?[A-Za-z])\b",
    r"(?<![-.\w])node(\.exe)?\s+(--?[A-Za-z]|\"?\$|\S+\.[mc]?[jt]s\b)",
    r"[\"\x27](node|npm|npx)(\.exe|\.cmd)?[\"\x27]",
    r"\bnodejs\.org/dist\b",
    r"\bnodesource\.com\b|\bsetup_[0-9]+\.x\b",
    r"\b(apt-get|apt|apk|brew|dnf|yum|choco|winget|scoop)\s+(install|add)\b.*\bnode(js)?\b",
]]

def scanned(path):
    name = path.rsplit("/", 1)[-1]
    return (path.startswith((".github/", "scripts/")) or path in ("rask.sh", "rask.ps1")
            or name.startswith("Dockerfile") or name.endswith((".targets", ".props", ".csproj")))

def code_lines(path, text):
    """The lines of a file a machine runs, comments blanked (line numbers kept)."""
    if path.endswith((".targets", ".props", ".csproj")):
        text = re.sub(r"<!--.*?-->", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
        marker = None
    elif path.endswith((".cs", ".ts", ".js")):
        text = re.sub(r"/\*.*?\*/", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
        marker = "//"
    elif path.endswith(".ps1"):
        text = re.sub(r"<#.*?#>", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
        marker = "#"
    else:
        marker = "#"
    for number, line in enumerate(text.split("\n"), 1):
        if marker and line.lstrip().startswith(marker):
            continue
        if marker == "#":
            line = re.sub(r"\s#\s.*$", "", line)
        elif marker == "//":
            line = re.sub(r"\s//\s.*$", "", line)
        yield number, line

failures = []
for path in sys.stdin.read().split("\0"):
    if not path or path == THIS:
        continue
    name = path.rsplit("/", 1)[-1]
    if (name in MANIFESTS or name.endswith((".mjs", ".cjs"))) and path not in PINS:
        failures.append(f"{path}: a Node file in the tree")
        continue
    if not scanned(path) or path.endswith((".md", ".txt", ".json", ".png", ".webp")):
        continue
    try:
        text = Path(path).read_text(encoding="utf-8")
    except (UnicodeDecodeError, FileNotFoundError):
        continue
    for number, line in code_lines(path, text):
        if any(p.search(line) for p in USES):
            failures.append(f"{path}:{number}: {line.strip()[:140]}")

for failure in failures:
    print(f"  FAIL {failure}", file=sys.stderr)
if failures:
    print(f"no node: {len(failures)} use(s) of Node in the tree", file=sys.stderr)
    sys.exit(1)
print("no node: nothing in the tree runs on Node")
'
