#!/usr/bin/env python3
"""Applies a build's RS0016 / RS0017 errors to the PublicAPI.Unshipped.txt baselines they are about.

The build records every public member in src/<Project>/PublicAPI/<tfm>/PublicAPI.Unshipped.txt, so a
member added without its line is RS0016 and a line left behind without its member is RS0017. Both errors
quote the exact line, which makes the fix mechanical:

    dotnet build Rask.slnx -c Release -warnaserror 2>&1 | tee /tmp/build.log
    python3 scripts/tools/apply-public-api.py /tmp/build.log
    python3 scripts/tools/apply-public-api.py --all-faces < /tmp/build.log    # stdin works too

RS0016 adds the quoted line to the baseline of the project AND target framework that reported it; RS0017
removes it. A multi-targeted project reports each face separately, so a full build fixes every face. With
--all-faces a change reported for one face is applied to every face the project has — for a project
whose faces carry the same surface (Rask.Ui), when only one of them was built.

A new line goes after the existing line it shares the longest prefix with, so a member lands beside its
type's other members instead of at the end of the file — which is also what keeps two branches that each
add members from colliding on the last line. The file is otherwise left exactly as it was.

scripts/public-api/record.py is the unattended twin: it runs the build itself with the two findings as
warnings. This one reads a log you already have, from a -warnaserror build, where they are errors.

Run the build again afterwards: a chain step exists only once its property compiles, so fixing one round
of errors can surface the next. --dry-run prints what would change. Exit code 0 when the log held nothing
to do or everything was applied, 1 when a baseline a diagnostic names cannot be found.
"""

from __future__ import annotations

import argparse
import bisect
import re
import sys
from collections import defaultdict
from pathlib import Path

DIAGNOSTIC = re.compile(
    r"^\s*(?:\d+>)?(?P<file>.+?)\((?P<line>\d+),(?P<column>\d+)\): (?:error|warning) (?P<id>RS0016|RS0017): "
    r"Symbol '(?P<symbol>.*)' (?:is not part of the declared public API"
    r"|is part of the declared API, but is either not public or could not be found)"
    r".*?\[(?P<project>[^\]]+?proj)(?:::TargetFramework=(?P<tfm>[^\]]+))?\]\s*$"
)
REMOVED = "*REMOVED*"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("logs", nargs="*", type=Path, help="build output; stdin when none is given")
    parser.add_argument("--all-faces", action="store_true", help="apply each change to every target framework of its project")
    parser.add_argument("--dry-run", action="store_true", help="print the changes and write nothing")
    args = parser.parse_args()

    text = "\n".join(log.read_text(encoding="utf-8", errors="replace") for log in args.logs) if args.logs else sys.stdin.read()
    changes, missing = collect(text, args.all_faces)
    for problem in sorted(missing):
        print(f"apply-public-api: {problem}", file=sys.stderr)

    if not changes and not missing:
        print("apply-public-api: no RS0016 / RS0017 in the build output.")
        return 0

    for baseline in sorted(changes):
        add, remove = changes[baseline]
        added, removed = apply(baseline, add, remove, args.dry_run)
        print(f"apply-public-api: {display(baseline)}: +{added} -{removed}{' (dry run)' if args.dry_run else ''}")
        if args.dry_run:
            for line in sorted(add):
                print(f"  + {line}")
            for line in sorted(remove):
                print(f"  - {line}")

    return 1 if missing else 0


def collect(text: str, all_faces: bool) -> tuple[dict[Path, tuple[set[str], set[str]]], set[str]]:
    """Baseline file -> (lines to add, lines to remove), from every diagnostic in the build output."""
    changes: dict[Path, tuple[set[str], set[str]]] = defaultdict(lambda: (set(), set()))
    missing: set[str] = set()
    # ANSI colour codes, when the output was captured from a terminal.
    for raw in re.sub(r"\x1b\[[0-9;]*m", "", text).splitlines():
        match = DIAGNOSTIC.match(raw)
        if not match:
            continue

        faces = baselines(Path(match["project"]), match["tfm"], all_faces)
        if not faces:
            missing.add(f"no PublicAPI/<tfm>/PublicAPI.Unshipped.txt for {match['project']} ({match['tfm'] or 'no target framework'})")
            continue

        for baseline in faces:
            changes[baseline][0 if match["id"] == "RS0016" else 1].add(match["symbol"])

    return changes, missing


def baselines(project: Path, tfm: str | None, all_faces: bool) -> list[Path]:
    """The baseline(s) one diagnostic is about: the reporting face's, or every face's."""
    root = project.parent / "PublicAPI"
    faces = sorted(path for path in root.glob("*/PublicAPI.Unshipped.txt"))
    if all_faces or tfm is None:
        # A single-targeted project's diagnostics carry no TargetFramework, and it has one face.
        return faces if all_faces or len(faces) == 1 else []

    own = root / tfm / "PublicAPI.Unshipped.txt"
    return [own] if own.exists() else []


def apply(baseline: Path, add: set[str], remove: set[str], dry_run: bool) -> tuple[int, int]:
    raw = baseline.read_bytes().decode("utf-8-sig")
    newline = "\r\n" if "\r\n" in raw else "\n"
    lines = raw.split(newline)
    trailing = lines and lines[-1] == ""
    if trailing:
        lines.pop()

    shipped_path = baseline.with_name("PublicAPI.Shipped.txt")
    shipped = set(shipped_path.read_text(encoding="utf-8-sig").splitlines()) if shipped_path.exists() else set()

    # A line that is in both sets was moved or re-declared within one build: it stays.
    add, remove = add - remove, remove - add
    present = set(lines)
    # A shipped member that is gone is recorded as removed; an unshipped one simply leaves the file.
    add |= {REMOVED + line for line in remove if line in shipped and line not in present}
    add -= present
    kept = [line for line in lines if line not in remove]
    removed = len(lines) - len(kept)

    result = insert(kept, sorted(add))
    if not dry_run and (add or removed):
        baseline.write_bytes((newline.join(result) + (newline if trailing else "")).encode("utf-8"))

    return len(add), removed


def insert(lines: list[str], new: list[str]) -> list[str]:
    """Each new line after the existing line it shares the longest prefix with; unrelated ones at the end."""
    ordered = sorted(set(line for line in lines if line and not line.startswith("#")))
    after: dict[str, list[str]] = defaultdict(list)
    tail: list[str] = []
    for line in new:
        anchor = nearest(ordered, line)
        (after[anchor] if anchor else tail).append(line)

    # The LAST occurrence of an anchor, so a run of related members grows at its end.
    last = {line: index for index, line in enumerate(lines)}
    result: list[str] = []
    for index, line in enumerate(lines):
        result.append(line)
        if last[line] == index:
            result.extend(after.get(line, ()))

    return result + tail


def nearest(ordered: list[str], line: str) -> str | None:
    """The line sharing the longest prefix, which in sorted order is always one of the two neighbours."""
    at = bisect.bisect_left(ordered, line)
    best, length = None, 0
    for candidate in ordered[max(at - 1, 0):at + 1]:
        shared = common_prefix(candidate, line)
        # Up to the type at least: sharing "Rask." or "static " alone relates nothing.
        if shared > length and "." in line[:shared].removeprefix("static "):
            best, length = candidate, shared

    return best


def common_prefix(a: str, b: str) -> int:
    size = min(len(a), len(b))
    for index in range(size):
        if a[index] != b[index]:
            return index

    return size


def display(path: Path) -> str:
    try:
        return str(path.resolve().relative_to(Path.cwd().resolve()))
    except ValueError:
        return str(path)


if __name__ == "__main__":
    sys.exit(main())
