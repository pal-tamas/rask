#!/usr/bin/env python3
"""Record a changed public surface in the PublicAPI baselines, without an IDE.

The build already says exactly what to write: RS0016 quotes the line a new member needs, RS0017 points
at the line whose member is gone. This applies both, which is what lets an unattended regeneration
(.github/workflows/upstream.yml) land a snapshot whose surface moved.

Usage:  scripts/public-api/record.py src/Rask.Core src/Rask.Web   # build each, then record
        scripts/public-api/record.py --log build.log              # record from a build already run
Exit:   0 recorded (or nothing to record) · 1 the build failed on something else
"""
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

MISSING = re.compile(r"warning RS0016: Symbol '(?P<line>.+)' is not part of the declared public API"
                     r".*\[(?P<project>[^\]:]+)(?:::TargetFramework=(?P<tfm>[^\]]+))?\]\s*$")
STALE = re.compile(r"^\s*(?P<file>.+PublicAPI\.\w+\.txt)\(\d+,\d+\): warning RS0017: Symbol '(?P<line>.+)' is part of")
FAILED = re.compile(r": error ")


def baseline(project, tfm):
    folder = Path(project).parent / "PublicAPI"
    if tfm is None:
        tfm = next(p.name for p in folder.iterdir() if p.is_dir())
    return folder / tfm / "PublicAPI.Unshipped.txt"


def changes(log):
    """What each baseline file gains and loses, read from the build's own diagnostics."""
    add, drop = defaultdict(set), defaultdict(set)
    for text in log.splitlines():
        if found := MISSING.search(text):
            add[baseline(found["project"], found["tfm"])].add(found["line"])
        elif found := STALE.search(text):
            drop[Path(found["file"])].add(found["line"])
    return add, drop


def apply(add, drop):
    for file in sorted(set(add) | set(drop)):
        head, *entries = file.read_text(encoding="utf-8").splitlines()
        kept = [e for e in entries if e not in drop[file]]
        new = sorted(add[file] - set(kept), key=str.casefold)
        for line in new:
            kept.insert(slot(kept, line), line)
        file.write_text("\n".join([head, *kept]) + "\n", encoding="utf-8")
        print(f"public api: {file}  +{len(new)} -{len(entries) + len(new) - len(kept)}")


def slot(entries, line):
    """Beside its neighbours rather than at the end: the files are kept in the IDE quick-fix's order, case ignored."""
    key = line.casefold()
    return next((i for i, entry in enumerate(entries) if entry.casefold() > key), len(entries))


def build(project):
    """One build with the two findings as warnings, so a project that references this one still compiles."""
    done = subprocess.run(
        ["dotnet", "build", project, "--no-incremental", "-nologo", "-clp:NoSummary",
         "-p:TreatWarningsAsErrors=false", "-p:RaskMdnRefresh=false"],
        capture_output=True, text=True, check=False)
    if done.returncode != 0 or FAILED.search(done.stdout):
        sys.stdout.write(done.stdout)
        sys.exit(1)
    return done.stdout


def main(args):
    logs = [Path(args[1]).read_text(encoding="utf-8")] if args[:1] == ["--log"] else [build(p) for p in args]
    for log in logs:
        apply(*changes(log))


if __name__ == "__main__":
    main(sys.argv[1:])
