#!/usr/bin/env python3
"""Decide which projects a set of changed files can reach.

Reads changed repo-relative paths on stdin (one per line) and writes to stdout either

    FULL<TAB><reason>

when the change is one whose blast radius this script refuses to reason about, or a list of
project paths (one per line) that must be built, and whose test assemblies must be run.

The contract is deliberately lopsided. Naming one project too many costs seconds; missing one
lets a break through the gate, and this repository's casebook is mostly gates that stopped
running without saying so. So every question this cannot answer precisely is answered FULL.

Two kinds of edge are followed, because ProjectReference alone is not the whole graph here:

  * <ProjectReference Include="..."/> - the ordinary one.
  * <Compile Include="..\\Other\\File.cs"/> - a source-linked file compiled into a second
    assembly. Rask uses this for Rask.Generators.Shared, Rask.Hosting.Shared, Shared.GeneratorTests,
    Shared.TestFiles and (as a "..\\Rask.Site\\**\\*.cs" glob) the site's own test project. A change
    to one of those files changes an assembly that nothing links to it by ProjectReference, so a
    graph built from ProjectReference alone would call it unaffected and be wrong.

    Globs are handled by taking the directory prefix before the first wildcard and treating the
    whole of it as the dependency. That is coarser than the glob and never narrower, which is the
    safe direction. MSBuild also splits an Include on ';', so each item is split the same way.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path

# A change to any of these can alter how EVERYTHING else builds or runs, so scoping is refused.
# Matched against the repo-relative path with forward slashes.
FULL_RUN_PATTERNS = [
    (re.compile(r"^Directory\.(Build|Packages)\.(props|targets)$"), "a repo-root MSBuild import"),
    (re.compile(r"^Directory\.Build\.rsp$"), "the repo-root MSBuild response file"),
    (re.compile(r"^[^/]+\.slnx$"), "the solution"),
    (re.compile(r"^(global\.json|NuGet\.config|nuget\.config)$"), "the SDK or NuGet pin"),
    (re.compile(r"^\.githooks/"), "a git hook"),
    (re.compile(r"^scripts/"), "a gate script"),
    (re.compile(r"^tests/Directory\.Build\.props$"), "the test-wide MSBuild import"),
    (re.compile(r"^tests/xunit\.runner\.json$"), "the test-wide runner configuration"),
    (re.compile(r"^src/[^/]+/build/"), "a packaged MSBuild import"),
    (re.compile(r"^\.editorconfig$"), "the formatting configuration"),
]

# Where projects live. A changed file outside these is not something this script maps to a project.
PROJECT_ROOTS = ("src/", "tests/", "site/", "benchmarks/")


def full(reason: str) -> None:
    sys.stdout.write(f"FULL\t{reason}\n")
    sys.exit(0)


def find_projects(root: Path) -> list[Path]:
    found: list[Path] = []
    for base in PROJECT_ROOTS:
        d = root / base
        if not d.is_dir():
            continue
        for path in d.rglob("*.csproj"):
            parts = path.relative_to(root).parts
            if "bin" in parts or "obj" in parts or ".claude" in parts:
                continue
            found.append(path)
    return found


def item_includes(text: str, tag: str) -> list[str]:
    """Every Include value for <tag ... Include="..."/>, split on ';' the way MSBuild does."""
    values: list[str] = []
    for raw in re.findall(rf"<{tag}\b[^>]*\sInclude\s*=\s*\"([^\"]+)\"", text):
        values.extend(part.strip() for part in raw.split(";") if part.strip())
    return values


def owning_project(rel: str, project_dirs: dict[str, Path]) -> Path | None:
    """The project whose directory is the nearest ancestor of this file."""
    d = os.path.dirname(rel)
    while d:
        if d in project_dirs:
            return project_dirs[d]
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    return None


def main() -> None:
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path.cwd()

    changed = [line.strip().replace("\\", "/") for line in sys.stdin if line.strip()]
    if not changed:
        sys.stdout.write("FULL\tno changed files were supplied\n")
        sys.exit(0)

    for rel in changed:
        for pattern, reason in FULL_RUN_PATTERNS:
            if pattern.search(rel):
                full(f"{rel} is {reason}")

    projects = find_projects(root)
    if not projects:
        full("no projects were found to scope against")

    # dir (repo-relative, forward slashes) -> project path
    project_dirs: dict[str, Path] = {}
    for p in projects:
        project_dirs[str(p.parent.relative_to(root)).replace("\\", "/")] = p

    # Edges. referenced -> {projects that depend on it}
    dependents: dict[str, set[str]] = {}
    # A source directory linked into a project by <Compile Include="..\..."/>.
    linked_dirs: list[tuple[str, str]] = []  # (directory, dependent project key)

    for p in projects:
        key = str(p.relative_to(root)).replace("\\", "/")
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            full(f"{key} could not be read")

        for inc in item_includes(text, "ProjectReference"):
            target = (p.parent / inc.replace("\\", "/")).resolve()
            try:
                tkey = str(target.relative_to(root)).replace("\\", "/")
            except ValueError:
                continue
            dependents.setdefault(tkey, set()).add(key)

        for inc in item_includes(text, "Compile"):
            norm = inc.replace("\\", "/")
            if not norm.startswith(".."):
                continue  # a file inside the project itself; the owning-project rule covers it
            # Cut the glob off: everything before the first wildcard segment.
            segments = norm.split("/")
            keep = [s for s in segments if "*" not in s and "?" not in s]
            if keep and ("." in keep[-1]) and keep[-1] == segments[len(keep) - 1] and len(keep) == len(segments):
                keep = keep[:-1]  # a concrete file: depend on its directory
            target = (p.parent / "/".join(keep)).resolve()
            try:
                tdir = str(target.relative_to(root)).replace("\\", "/")
            except ValueError:
                continue
            linked_dirs.append((tdir, key))

    affected: set[str] = set()
    for rel in changed:
        if not rel.startswith(PROJECT_ROOTS):
            full(f"{rel} is outside src/, tests/, site/ and benchmarks/")

        owner = owning_project(rel, project_dirs)
        if owner is None:
            full(f"{rel} belongs to no project (a source-linked or shared file)")
        affected.add(str(owner.relative_to(root)).replace("\\", "/"))

        for tdir, dependent in linked_dirs:
            if rel == tdir or rel.startswith(tdir + "/"):
                affected.add(dependent)

    # Transitive closure over "is depended upon by".
    queue = list(affected)
    while queue:
        current = queue.pop()
        for dep in dependents.get(current, ()):
            if dep not in affected:
                affected.add(dep)
                queue.append(dep)

    for key in sorted(affected):
        sys.stdout.write(key + "\n")


if __name__ == "__main__":
    main()
