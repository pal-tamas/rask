#!/usr/bin/env python3
"""Find every use of Node in a list of files: what scripts/tests/no-node.test.sh holds the tree to.

Reads NUL-separated repo-relative paths on stdin (as `git ls-files -z` writes them), reads each file from the
current directory and prints one `path[:line]: why` per use; exits 1 when there is one.

A use is a Node file in the tree, or something that RUNS Node or its package managers:

  - a tracked npm/Node file: package.json, package-lock.json, npm-shrinkwrap.json, yarn.lock, pnpm-lock.yaml,
    bun.lock(b), .npmrc, .nvmrc, .node-version, *.mjs, *.cjs;
  - node, nodejs, npm, npx, pnpm, yarn, bun or corepack in COMMAND POSITION of a shell line — a script, a
    workflow's or an action's `run:`, a Dockerfile's RUN, an MSBuild <Exec Command>, a PowerShell line;
  - actions/setup-node, a Node download (nodejs.org/dist, nodesource), or a system package manager installing it;
  - a .NET or Python process started on one of those programs.

The WORD is not a use: a JSON key "node" or a DOM node in C#, "npm's latest" in a message, a URL of npm's
registry (registry.npmjs.org is data, read over HTTPS) and a comment saying why Node is gone are all left alone.
"""

import re
import sys
from pathlib import Path

NODE_FILES = {"package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml",
              "bun.lock", "bun.lockb", ".npmrc", ".nvmrc", ".node-version"}
PROGRAMS = r"(?:node|nodejs|npm|npx|pnpm|yarn|bun|corepack)(?:\.exe|\.cmd)?"
SYSTEM_INSTALLERS = r"(?:apt-get|apt|apk|brew|dnf|yum|choco|winget|scoop)"

# Where a command starts on a shell line: the line itself, after an operator, a substitution, a keyword that
# runs what follows, or a yml `run:`/list dash. Prefixes that hand on to a command (sudo, exec, env A=b) are
# skipped by COMMAND below.
_START = r"(?:^|[;&|(`{]|\$\(|\bthen\s|\bdo\s|\belse\s|\brun:\s|^\s*-\s)"
_PREFIX = r"(?:(?:sudo|exec|command|time|nohup|xargs(?:\s+-\S+)*|env(?:\s+\w+=\S*)*|\w+=\S*)\s+)*"
COMMAND = re.compile(_START + r"\s*" + _PREFIX + r"(" + PROGRAMS + r")(?=\s|$|[;&|)])")
INSTALL = re.compile(_START + r"\s*" + _PREFIX + SYSTEM_INSTALLERS + r"\s+(?:install|add)\b[^;&|]*\bnode(?:js)?\b")
EVERYWHERE = [
    (re.compile(r"\bactions/setup-node\b"), "sets Node up"),
    (re.compile(r"\bnodejs\.org/dist\b|\bnodesource\.com\b"), "downloads Node"),
]
# A process started on a program: `FileName = "npm"`, `new ProcessStartInfo("node", …)`, `Process.Start("npx")`,
# a bare "npm.cmd"/"node.exe" literal (they exist only to be run), Python's `["node", …]`/`run("npm …")`.
DOTNET_START = re.compile(r"(?:FileName\s*=|ProcessStartInfo\(|Process\.Start\(|Cli\.Wrap\()\s*\"" + PROGRAMS + r"\""
                          r"|\"(?:node\.exe|npm\.cmd|npx\.cmd|pnpm\.cmd|yarn\.cmd)\"")
PYTHON_START = re.compile(r"\[\s*[\"']" + PROGRAMS + r"[\"']\s*[,\]]"
                          r"|\b(?:run|call|check_call|check_output|Popen|system)\(\s*[\"']" + PROGRAMS + r"(?:\s|[\"'])")

SHELL_SUFFIXES = (".sh", ".bash", ".yml", ".yaml", ".ps1", ".psm1")
MSBUILD_SUFFIXES = (".targets", ".props", ".csproj")


def node_file(path):
    name = path.rsplit("/", 1)[-1]
    return name in NODE_FILES or name.endswith((".mjs", ".cjs"))


def kind(path, text):
    """How a file is read: shell, powershell, msbuild, dockerfile, csharp, python — or None, not read."""
    name = path.rsplit("/", 1)[-1]
    if name.startswith("Dockerfile"):
        return "dockerfile"
    if path.endswith((".ps1", ".psm1")):
        return "powershell"
    if path.endswith(SHELL_SUFFIXES):
        return "shell"
    if path.endswith(MSBUILD_SUFFIXES):
        return "msbuild"
    if path.endswith(".cs"):
        return "csharp"
    if path.endswith(".py"):
        return "python"
    if "." not in name and re.match(r"#!\s*/(?:usr/)?bin/(?:env\s+)?(?:ba|z)?sh\b", text):
        return "shell"
    if "." not in name and re.match(r"#!\s*/(?:usr/)?bin/(?:env\s+)?python", text):
        return "python"
    return None


def blank(match):
    return "\n" * match.group(0).count("\n")


def without_hash_comment(line):
    """A shell, yml, PowerShell or Python line with its comment cut off (a # that opens a word)."""
    return "" if line.lstrip().startswith("#") else re.sub(r"(^|\s)#.*$", "", line)


def shell_text(line):
    """A shell line as commands see it: single-quoted text dropped (nothing runs inside it), double-quoted text
    kept only where it substitutes a command."""
    line = re.sub(r"'[^']*'", "''", line)
    return re.sub(r"\"(?:[^\"\\]|\\.)*\"", lambda m: '"' + " ".join(re.findall(r"\$\([^)]*\)", m.group(0))) + '"', line)


def lines(path, text, how):
    """(line number, text a machine runs) for every line of the file, comments and prose taken out."""
    if how == "msbuild":
        text = re.sub(r"<!--.*?-->", blank, text, flags=re.S)
        for exec_ in re.finditer(r"<Exec\b[^>]*?\bCommand\s*=\s*\"([^\"]*)\"", text, flags=re.S):
            first = text.count("\n", 0, exec_.start(1)) + 1
            command = exec_.group(1).replace("&amp;", "&").replace("&quot;", "\"").replace("&apos;", "'")
            for offset, line in enumerate(command.split("\n")):
                yield first + offset, "shell", line
        for number, line in enumerate(text.split("\n"), 1):
            yield number, "msbuild", line
        return
    if how == "csharp":
        text = re.sub(r"/\*.*?\*/", blank, text, flags=re.S)
        for number, line in enumerate(text.split("\n"), 1):
            yield number, how, re.sub(r"(^|\s)//.*$", "", line)
        return
    if how == "powershell":
        text = re.sub(r"<#.*?#>", blank, text, flags=re.S)
    for number, line in enumerate(text.split("\n"), 1):
        line = without_hash_comment(line)
        if how == "dockerfile":
            line = re.sub(r"^\s*RUN\s+", "", line) if re.match(r"^\s*RUN\s", line) or line.startswith((" ", "\t")) else ""
            how_line = "shell"
        else:
            how_line = how
        yield number, how_line, line


def uses(how, line):
    for pattern, why in EVERYWHERE:
        if pattern.search(line):
            yield why
    if how in ("shell", "powershell"):
        runs = shell_text(line)
        if how == "powershell":
            runs = re.sub(r"\bGet-Command\s+", "& ", runs)
        if (found := COMMAND.search(runs)) is not None:
            yield f"runs {found.group(1)}"
        if INSTALL.search(runs):
            yield "installs Node"
    elif how == "csharp" and DOTNET_START.search(line):
        yield "starts a Node process"
    elif how == "python" and PYTHON_START.search(line):
        yield "starts a Node process"


def scan(paths, read):
    """Every use in PATHS, read by READ(path) -> text (None when it is not text)."""
    for path in paths:
        if node_file(path):
            yield f"{path}: a Node file in the tree"
            continue
        text = read(path)
        if text is None:
            continue
        how = kind(path, text)
        if how is None:
            continue
        for number, how_line, line in lines(path, text, how):
            for why in uses(how_line, line):
                yield f"{path}:{number}: {why}: {line.strip()[:140]}"


def read(path):
    try:
        return Path(path).read_text(encoding="utf-8")
    except (UnicodeDecodeError, FileNotFoundError, IsADirectoryError):
        return None


def main():
    skip = set(sys.argv[1:])
    paths = [p for p in sys.stdin.read().split("\0") if p and p not in skip]
    found = list(scan(paths, read))
    for use in found:
        print(f"  FAIL {use}", file=sys.stderr)
    if found:
        print(f"no node: {len(found)} use(s) of Node in the tree", file=sys.stderr)
        return 1
    print("no node: nothing in the tree runs on Node")
    return 0


if __name__ == "__main__":
    sys.exit(main())
