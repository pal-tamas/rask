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
    EmbeddedResource / None / Content / AdditionalFiles items reaching out of the project are
    build inputs in exactly the same way, and are followed the same way.

  * <RaskTestReads Include="..."/> - a file a TEST reads from disk at runtime (a contract test on
    another project's rask.ts, a scan of docs/**/*.md). No reference carries that edge, so the test
    project declares it, and the glob is matched precisely, extension filter included. This is also
    what lets a file outside src/ and tests/ - CHANGELOG.md, docs/, llms.txt - scope to the tests
    that read it instead of forcing FULL. A file outside them that nobody declares is still FULL.
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

# Where projects live, and now the ONLY two places they live: src/ is what ships, tests/ is what
# verifies or measures (the benchmarks moved in there too). A changed file outside these is not
# something this script maps to a project, and is answered FULL.
PROJECT_ROOTS = ("src/", "tests/")


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
            # src/Rask.Templates/ is the scaffolder's PAYLOAD, not projects of ours: every tree holds
            # a Company.RaskServer.csproj. Treating them as projects makes the map ambiguous (the
            # same name once per template) and would attribute an edit to a template to a project
            # that is never built here.
            if "Rask.Templates" in parts:
                continue
            found.append(path)
    return found


def item_includes(text: str, tag: str) -> list[str]:
    """Every Include value for <tag ... Include="..."/>, split on ';' the way MSBuild does."""
    values: list[str] = []
    for raw in re.findall(rf"<{tag}\b[^>]*\sInclude\s*=\s*\"([^\"]+)\"", text):
        values.extend(part.strip() for part in raw.split(";") if part.strip())
    return values


def repo_relative(project_dir: str, include: str) -> str:
    """An Include resolved against its project's directory, repo-relative, wildcards kept."""
    return os.path.normpath(os.path.join(project_dir, include.replace("\\", "/"))).replace("\\", "/")


def glob_regex(pattern: str) -> re.Pattern[str]:
    """An MSBuild glob as a regex over repo-relative paths: ** spans directories, * and ? do not."""
    out = []
    i = 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif pattern[i] == "*":
            out.append("[^/]*")
            i += 1
        elif pattern[i] == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    return re.compile("^" + "".join(out) + "$")


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
    # A file a test reads at runtime, declared by <RaskTestReads/>, matched exactly.
    test_reads: list[tuple[re.Pattern[str], str]] = []

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

        project_dir = str(p.parent.relative_to(root)).replace("\\", "/")

        for tag in ("Compile", "EmbeddedResource", "None", "Content", "AdditionalFiles"):
            for inc in item_includes(text, tag):
                norm = inc.replace("\\", "/")
                if not norm.startswith(".."):
                    continue  # a file inside the project itself; the owning-project rule covers it
                # Cut the glob off: everything before the first wildcard (or MSBuild property) segment.
                segments = norm.split("/")
                keep = []
                for s in segments:
                    if "*" in s or "?" in s or "$(" in s:
                        break
                    keep.append(s)
                target = repo_relative(project_dir, "/".join(keep))
                if len(keep) == len(segments) and "." in keep[-1]:
                    parent = os.path.dirname(target)
                    # A concrete file: depend on its directory — unless that is the repo root, where
                    # "its directory" would be every file there is; there, the file itself.
                    target = parent if parent else target
                if target.startswith("../") or target == "..":
                    continue
                linked_dirs.append((target, key))

        for inc in item_includes(text, "RaskTestReads"):
            target = repo_relative(project_dir, inc)
            if not target.startswith("../"):
                test_reads.append((glob_regex(target), key))

    affected: set[str] = set()
    for rel in changed:
        readers = {dependent for tdir, dependent in linked_dirs if rel == tdir or rel.startswith(tdir + "/")}
        readers |= {reader for pattern, reader in test_reads if pattern.match(rel)}
        affected |= readers

        if not rel.startswith(PROJECT_ROOTS):
            if readers:
                continue  # CHANGELOG.md, docs/, llms.txt: exactly the projects that read it
            full(f"{rel} is outside src/ and tests/, and no project declares reading it")

        # The template trees hold no project of their own: they are embedded into the CLI, which is
        # what has to rebuild (and be retested) when one of them changes. Without this they map to
        # nothing and every template edit answers FULL, which is safe and needlessly expensive —
        # editing a template is meant to be the cheap, ordinary way to change what `rask new` writes.
        if rel.startswith("src/Rask.Templates/"):
            # The csproj, not the directory: everything downstream passes these straight to MSBuild,
            # which answers MSB3202 ("project file was not found") for a directory.
            affected.add("src/Rask.Cli/Rask.Cli.csproj")
            continue

        owner = owning_project(rel, project_dirs)
        if owner is None:
            full(f"{rel} belongs to no project (a source-linked or shared file)")
        affected.add(str(owner.relative_to(root)).replace("\\", "/"))

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
