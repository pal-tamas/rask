#!/usr/bin/env python3
"""Remember which test projects passed on which tree, so the next gate need not run them again.

Two scoped runs of scripts/run-unit-local.sh minutes apart, on a tree that has not moved, are the same
build and the same tests. A
project's pass is reusable when nothing that can reach it changed since, and "what can reach it" is
already answered in one place, scripts/lib/affected_projects.py. So this holds no graph of its own. A
stamp is the tree a project last passed on; the question is put to the scoper as a diff from that
tree to the one in front of us, and FULL, an error or a missing object all mean "run it".

The tree is the WORKING tree (tracked and untracked, ignored files left out), not HEAD and not the
index, because the working tree is what the tests were built from.

    gate_stamps.py tree   <root>                    print the working tree's id
    gate_stamps.py reuse  <root> <tree> <salt>      projects on stdin -> those that may be skipped
    gate_stamps.py record <root> <tree> <salt>      projects on stdin -> stamped as passed

<salt> is everything outside the tree that a pass depends on (the SDK version, the configuration).
"""

from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from pathlib import Path

STAMP_DIR = "artifacts/gate-stamps"


def git(root: Path, *args: str, env: dict[str, str] | None = None, stdin: str | None = None) -> str:
    result = subprocess.run(
        ["git", "-C", str(root), *args], input=stdin, capture_output=True, text=True, env=env, check=True
    )
    return result.stdout


def working_tree(root: Path) -> str:
    """The id of a tree holding exactly what is on disk, written through a throwaway index."""
    git_dir = Path(git(root, "rev-parse", "--absolute-git-dir").strip())
    with tempfile.TemporaryDirectory() as tmp:
        index = Path(tmp) / "index"
        real = Path(os.environ.get("GIT_INDEX_FILE") or git_dir / "index")  # a hook is handed its own
        if real.exists():
            index.write_bytes(real.read_bytes())  # start from the real one, so unchanged files are not re-hashed
        env = {**os.environ, "GIT_INDEX_FILE": str(index)}
        git(root, "add", "-A", env=env)
        return git(root, "write-tree", env=env).strip()


def stamp_path(root: Path, project: str) -> Path:
    return root / STAMP_DIR / (project.replace("/", "__") + ".stamp")


def read_stamp(root: Path, project: str) -> tuple[str, str] | None:
    try:
        tree, salt = stamp_path(root, project).read_text(encoding="utf-8").split("\n")[:2]
    except (OSError, ValueError):
        return None
    return tree, salt


def reached_since(root: Path, stamped: str, tree: str) -> set[str] | None:
    """The projects a move from <stamped> to <tree> can reach, or None when that cannot be narrowed."""
    if stamped == tree:
        return set()
    try:
        changed = git(root, "diff", "--name-only", "--no-renames", stamped, tree)
    except subprocess.CalledProcessError:
        return None  # the stamped tree was pruned, or was never a tree
    if not changed.strip():
        return set()

    scoper = Path(__file__).with_name("affected_projects.py")
    result = subprocess.run(
        [sys.executable, str(scoper), str(root)], input=changed, capture_output=True, text=True, check=False
    )
    if result.returncode != 0 or result.stdout.startswith("FULL"):
        return None
    return {line for line in result.stdout.splitlines() if line}


def reusable(root: Path, tree: str, salt: str, projects: list[str]) -> list[str]:
    reached: dict[str, set[str] | None] = {}  # one scoper call per distinct stamped tree
    keep = []
    for project in projects:
        stamp = read_stamp(root, project)
        if stamp is None or stamp[1] != salt:
            continue
        if stamp[0] not in reached:
            reached[stamp[0]] = reached_since(root, stamp[0], tree)
        hit = reached[stamp[0]]
        if hit is not None and project not in hit:
            keep.append(project)
    return keep


def record(root: Path, tree: str, salt: str, projects: list[str]) -> None:
    (root / STAMP_DIR).mkdir(parents=True, exist_ok=True)
    for project in projects:
        stamp_path(root, project).write_text(f"{tree}\n{salt}\n", encoding="utf-8")


def main() -> None:
    command, root = sys.argv[1], Path(sys.argv[2]).resolve()
    if command == "tree":
        print(working_tree(root))
        return

    tree, salt = sys.argv[3], sys.argv[4]
    projects = [line.strip() for line in sys.stdin if line.strip()]
    if command == "reuse":
        print("\n".join(reusable(root, tree, salt, projects)))
    elif command == "record":
        record(root, tree, salt, projects)
    else:
        sys.exit(f"gate_stamps: unknown command {command}")


if __name__ == "__main__":
    main()
