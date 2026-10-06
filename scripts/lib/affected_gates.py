#!/usr/bin/env python3
"""Decide which CI gates a set of changed files can reach.

Reads changed repo-relative paths on stdin (one per line) and writes one line to stdout: either

    FULL<TAB><reason>

or the gate keys the change reaches, space-separated. .github/workflows/gates.yml tags each gate
with the key that selects it (`"when"`), so the list of gates stays there and only the reasoning
lives here.

This asks scripts/lib/affected_projects.py which PROJECTS the change reaches and holds no graph of
its own; every FULL that script answers is FULL here. What it adds is the step from projects to
gates, for the gates whose subject is not a test project's references:

    code       anything at all reached           -> the analyzer build, the unit tests
    cs         a .cs file changed                -> the formatter
    site       the published site is reached     -> the browser journeys over it
    server     the Rask.Server journeys' project
    devtools   the devtools journeys or their fixtures
    sqlite     the browser SQLite journeys or their fixture
    datademo   the data demo app or its journeys
    packaging  the CLI, the templates, or how a project is built and packed (by FILE, see below)
                                                 -> the CLI build and the templates

The contract is the same lopsided one: a gate too many costs minutes on a runner, a gate too few
lets a break through until the next full run. A push to main is scoped; the full set runs on a
schedule (.github/workflows/full.yml), and publishing follows that run, not a scoped one.
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

# gate key -> the projects (directory prefixes) whose being reached selects it.
PROJECT_GATES = {
    "site": ("src/Rask.Site/", "tests/Rask.Site.E2E.Tests/"),
    "server": ("tests/Rask.Server.E2E.Tests/",),
    "devtools": ("tests/Rask.DevTools.E2E.Tests/", "tests/Rask.DevTools.Fixture.Wasm/", "tests/Rask.DevTools.Fixture.Server/"),
    "sqlite": ("tests/Rask.SQLite.Browser.E2E.Tests/", "tests/Rask.SQLite.Browser.Fixture.Wasm/"),
    "datademo": ("src/Rask.Site.DataDemo/", "tests/Rask.Site.DataDemo.E2E.Tests/"),
}

# The CLI build and template gates are selected by the FILES that changed, not by the projects reached,
# and that is a deliberate narrowing — the only one here that is narrower than the graph. Their test
# projects reference Rask.Server and Rask.Core, so the graph says every source change reaches them, and
# they are the longest gates there are: honoured, a scoped run would be no shorter than a whole one for
# any framework change. What they prove is that the packages pack and a scaffold builds against them,
# which is broken by the CLI, the templates, or how a project is built and packed — the paths below.
# A package broken by an ordinary .cs change is found by the next whole run (full.yml), not this push;
# TemplatesCompileTests, in the unit gate, compiles the templates against the assemblies on every one.
PACKAGING_FILES = re.compile(
    r"^(src/Rask\.Cli/|src/Rask\.Templates/|tests/Rask\.Cli\.E2E\.Tests/|tests/Rask\.Templates\.E2E\.Tests/"
    r"|tests/Rask\.Cli\.Tests/(CliBuildE2E|RepoPins|DeployE2E|DeployHostFixture|TestDoubles)\.cs$"
    r"|src/.*\.(csproj|props|targets)$)"
)


def reached_projects(root: Path, changed: list[str]) -> tuple[str | None, list[str]]:
    """(reason, []) when the scoper answers FULL, otherwise (None, projects)."""
    out = subprocess.run(
        [sys.executable, str(root / "scripts/lib/affected_projects.py"), str(root)],
        input="\n".join(changed) + "\n", capture_output=True, text=True, check=True,
    ).stdout
    lines = [line for line in out.splitlines() if line.strip()]
    if not lines:
        return "the graph named no projects", []
    if lines[0].startswith("FULL"):
        return lines[0].partition("\t")[2] or "the scoper refused to narrow", []
    return None, lines


def gates_for(changed: list[str], projects: list[str]) -> list[str]:
    keys = ["code"]
    if any(path.endswith(".cs") for path in changed):
        keys.append("cs")
    for key, prefixes in PROJECT_GATES.items():
        if any(project.startswith(prefixes) for project in projects):
            keys.append(key)
    if any(PACKAGING_FILES.match(path) for path in changed):
        keys.append("packaging")
    return keys


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    changed = [line.strip().replace("\\", "/") for line in sys.stdin if line.strip()]
    if not changed:
        sys.stdout.write("FULL\tno changed files were supplied\n")
        return

    # The CI definition decides what every gate runs. A test may declare a workflow as something it
    # reads, which would scope a change to it down to that one test project — the one change that must
    # never be judged by a narrowed run of itself.
    for path in changed:
        if path.startswith(".github/"):
            sys.stdout.write(f"FULL\t{path} is part of the CI definition\n")
            return

    reason, projects = reached_projects(root, changed)
    if reason is not None:
        sys.stdout.write(f"FULL\t{reason}\n")
        return

    sys.stdout.write(" ".join(gates_for(changed, projects)) + "\n")


if __name__ == "__main__":
    main()
