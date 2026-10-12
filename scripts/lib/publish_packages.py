#!/usr/bin/env python3
"""Push a packed set of .nupkg files so that a feed never lists a package ahead of its dependencies.

    publish_packages.py plan   <dir> [--projects src]
    publish_packages.py push   <dir> --source <feed> [--api-key-env NAME] [--gate-index nuget.org]
                                     [--projects src] [--log push.log] [--commit <sha>]
    publish_packages.py verify <dir> [--index nuget.org] [--wait 1800]

Why it exists (2026-10-12): `dotnet nuget push "./artifacts/*.nupkg"` pushes in file-name order and
stops at the first failure. A network stall left Rask.Server listed at a version Rask itself had not
reached, and every consumer floating `0.*-*` failed restore with NU1102 until the next nightly.

What `push` does about it:
  order   leaf-most first, read from the packed nuspecs -- there is no list here to go stale.
  gate    with --gate-index, a package is not pushed until the feed SERVES everything it depends on.
          Accepted is not served: nuget.org validates for minutes, in no particular order.
  retry   one package per push command, retried with backoff; a duplicate is success, so a re-run
          resumes where the last one stopped.

Exit status: 0 done, 1 a push or a wait failed, 2 the packed set itself is wrong.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shlex
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path

# The flat container, not the search or registration index. It is what restore reads to resolve a
# version (a floating `0.*-*` included) and to download, so "listed here" is exactly "NU1102 is over".
# Search lags it by many minutes, and registration by a few (scripts/verify-release-restore.sh).
INDEXES = {"nuget.org": "https://api.nuget.org/v3-flatcontainer/{id}/index.json"}

REFUSED = {400, 401, 403, 413}   # a second try gets the same answer


class SetError(Exception):
    """The packed set cannot be published as it is."""


@dataclass
class Package:
    id: str
    version: str
    path: Path
    commit: str
    depends_on: set[str] = field(default_factory=set)   # lower-cased ids, inside the set only
    level: int = 0

    @property
    def key(self) -> str:
        return self.id.lower()


# ------------------------------------------------------------------ the set and its order

def read_nuspec(path: Path) -> tuple[str, str, str, list[tuple[str, str]]]:
    """(id, version, commit, [(dependency id, version range)]) of one .nupkg."""
    with zipfile.ZipFile(path) as archive:
        name = next((n for n in archive.namelist() if "/" not in n and n.lower().endswith(".nuspec")), None)
        if name is None:
            raise SetError(f"{path.name} holds no .nuspec")
        root = ET.fromstring(archive.read(name))
    for node in root.iter():
        node.tag = node.tag.rpartition("}")[2]   # the nuspec namespace changes with the schema version
    meta = root.find("metadata")
    repository = meta.find("repository")
    dependencies = [(d.get("id", ""), d.get("version", "")) for d in meta.iter("dependency")]
    return (meta.findtext("id", "").strip(), meta.findtext("version", "").strip(),
            (repository.get("commit", "") if repository is not None else ""), dependencies)


def load_set(directory: Path) -> list[Package]:
    """Every package in the directory, ordered: level 0 depends on nothing in the set."""
    files = sorted(directory.glob("*.nupkg"))
    if not files:
        raise SetError(f"no .nupkg in {directory}")

    packages: dict[str, Package] = {}
    declared: dict[str, list[tuple[str, str]]] = {}
    for path in files:
        pid, version, commit, dependencies = read_nuspec(path)
        if pid.lower() in packages:
            raise SetError(f"{pid} is packed twice: {packages[pid.lower()].path.name} and {path.name}")
        packages[pid.lower()] = Package(pid, version, path, commit)
        declared[pid.lower()] = dependencies

    # One pack is one version from one commit. Two means the directory holds a leftover.
    for what, values in (("version", {p.version for p in packages.values()}),
                         ("commit", {p.commit for p in packages.values() if p.commit})):
        if len(values) > 1:
            raise SetError(f"the set carries more than one {what}: {', '.join(sorted(values))}")

    version = next(iter(packages.values())).version
    for key, dependencies in declared.items():
        for dep_id, dep_range in dependencies:
            if dep_id.lower() in packages:
                if dep_id.lower() != key:
                    packages[key].depends_on.add(dep_id.lower())
            elif version in re.split(r"[\[\]\(\),\s]+", dep_range):
                # A dependency at the set's own version is one of ours. Not packed, it is the hole
                # that left `rask new` unable to restore on 0.20.0 (#1044).
                raise SetError(f"{packages[key].id} depends on {dep_id} {version}, which was not packed")

    assign_levels(packages)
    return sorted(packages.values(), key=lambda p: (p.level, p.key))


def declared_ids(projects: Path) -> set[str]:
    """The packages the tree says it ships: every project not marked <IsPackable>false</IsPackable>."""
    ids = set()
    for project in projects.glob("*/*.csproj"):
        text = project.read_text(encoding="utf-8")
        if re.search(r"<IsPackable>\s*false\s*</IsPackable>", text, re.I):
            continue
        named = re.search(r"<PackageId>\s*([^<$]+?)\s*</PackageId>", text)
        ids.add(named.group(1) if named else project.stem)
    return ids


def check_against_projects(packages: list[Package], projects: Path | None) -> None:
    """The pack has no list to go stale; this asks that it packed what the tree declares, no more, no less."""
    if projects is None:
        return
    declared, packed = declared_ids(projects), {p.id for p in packages}
    if declared != packed:
        raise SetError(f"the packed set is not the one {projects} declares -- "
                       f"declared and not packed: {', '.join(sorted(declared - packed)) or 'none'}; "
                       f"packed and not declared: {', '.join(sorted(packed - declared)) or 'none'}")


def assign_levels(packages: dict[str, Package]) -> None:
    """level = the longest chain of dependencies beneath a package."""
    state: dict[str, int] = {}   # 1 while being walked, 2 when done

    def walk(key: str, trail: list[str]) -> int:
        if state.get(key) == 2:
            return packages[key].level
        if state.get(key) == 1:
            cycle = trail[trail.index(key):] + [key]
            raise SetError("the packages depend on each other in a circle: " + " -> ".join(packages[k].id for k in cycle))
        state[key] = 1
        package = packages[key]
        package.level = 1 + max((walk(dep, trail + [key]) for dep in sorted(package.depends_on)), default=-1)
        state[key] = 2
        return package.level

    for key in sorted(packages):
        walk(key, [])


def levels_of(packages: list[Package]) -> list[list[Package]]:
    levels: list[list[Package]] = [[] for _ in range(packages[-1].level + 1)]
    for package in packages:
        levels[package.level].append(package)
    return levels


# ------------------------------------------------------------------ one push, retried

def classify(exit_code: int, output: str) -> str:
    """ok | refused | throttled | transient -- from what `dotnet nuget push` printed.

    Read from the status WORDS, never a bare number: the file name carries the version, and
    0.23.1-alpha.0.403 is not a Forbidden.
    """
    low = output.lower()
    status = re.search(r"status code does not indicate success: (\d{3})", low)
    code = int(status.group(1)) if status else 0
    if exit_code == 0 or code == 409:
        return "ok"   # a duplicate is the package being there, which is what was asked for
    if code == 429 or "too many requests" in low or "toomanyrequests" in low:
        return "throttled"
    if code in REFUSED or re.search(r"^\s*(forbidden|unauthorized|badrequest)\b", low, re.M):
        return "refused"
    return "transient"   # a timeout, a 5xx, a dropped connection, or anything unrecognised


def wait_before_retry(kind: str, attempt: int, output: str, args: argparse.Namespace) -> float:
    """Seconds to wait after failed attempt number `attempt` (1-based): doubling, capped."""
    wait = args.backoff * 2 ** (attempt - 1)
    if kind == "throttled":
        # The CLI does not print the Retry-After header; when it is there, it wins over the guess.
        asked = re.search(r"retry-after\D{0,3}(\d+)", output, re.I)
        wait = float(asked.group(1)) if asked else max(wait, args.throttle_wait)
    return min(wait, args.max_wait)


def push_one(path: Path, args: argparse.Namespace, key: str, out_lock: threading.Lock) -> bool:
    command = shlex.split(os.environ.get("PUSH_CMD", "dotnet nuget push")) + [
        # The .snupkg beside a package rides along: the CLI pushes it after its package, and a
        # retry that finds the package already there still sends the symbols.
        str(path), "--source", args.source, "--skip-duplicate", "--timeout", str(args.timeout),
    ]
    if key:
        command += ["--api-key", key]

    for attempt in range(1, args.attempts + 1):
        try:
            # The CLI's own --timeout is per request; this is the backstop for one that never returns.
            done = subprocess.run(command, capture_output=True, text=True, timeout=args.timeout + 120)
            exit_code, output = done.returncode, done.stdout + done.stderr
        except subprocess.TimeoutExpired as hung:
            exit_code, output = 1, f"{hung.stdout or ''}{hung.stderr or ''}\nno answer after {args.timeout + 120} s"
        if key:
            output = output.replace(key, "***")

        kind = classify(exit_code, output)
        last = kind in ("ok", "refused") or attempt == args.attempts
        wait = 0.0 if last else wait_before_retry(kind, attempt, output, args)
        with out_lock:
            print(output.rstrip())
            if kind == "ok":
                if args.log:
                    # Only the attempt that counted: scripts/lib/push_verdict.sh reads the FIRST
                    # section it finds for a file.
                    with open(args.log, "a", encoding="utf-8") as log:
                        log.write(output.rstrip() + "\n")
            elif last:
                print(f"!!  {path.name}: {kind}, attempt {attempt} of {args.attempts} -- giving up", flush=True)
            else:
                print(f"    {path.name}: {kind}, attempt {attempt} of {args.attempts} -- retrying in {wait:g} s", flush=True)
        if kind == "ok":
            return True
        if last:
            return False
        time.sleep(wait)
    return False


def push_all(files: list[Path], args: argparse.Namespace, key: str) -> list[str]:
    """Push files that do not depend on each other, a few at a time; the names that failed."""
    out_lock = threading.Lock()
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        results = list(pool.map(lambda path: push_one(path, args, key, out_lock), files))
    return [path.name for path, ok in zip(files, results) if not ok]


# ------------------------------------------------------------------ is it served yet

def is_served(package: Package, index: str) -> bool:
    url = INDEXES.get(index, index).format(id=package.key)
    try:
        request = urllib.request.Request(url, headers={"Cache-Control": "no-cache"})
        with urllib.request.urlopen(request, timeout=20) as response:
            versions = json.load(response).get("versions", [])
    except (urllib.error.URLError, OSError, ValueError):
        return False   # 404 is a package id the feed has never served; anything else is asked again
    return package.version.lower().partition("+")[0] in (v.lower() for v in versions)


def wait_until_served(packages: list[Package], index: str, wait: float, poll: float, why: str) -> list[Package]:
    """Poll until every package is served or `wait` seconds pass; the ones still missing."""
    missing = list(packages)
    deadline = time.monotonic() + wait
    while True:
        with ThreadPoolExecutor(max_workers=8) as pool:   # one GET each, independent
            served = list(pool.map(lambda p: is_served(p, index), missing))
        missing = [p for p, ok in zip(missing, served) if not ok]
        if not missing or time.monotonic() >= deadline:
            return missing
        print(f"    {why}: waiting for {len(missing)} -- {', '.join(p.id for p in missing[:6])}"
              f"{' ...' if len(missing) > 6 else ''}", flush=True)
        time.sleep(min(poll, max(0.0, deadline - time.monotonic())))


# ------------------------------------------------------------------ commands

def summary(text: str) -> None:
    print(text, flush=True)
    target = os.environ.get("GITHUB_STEP_SUMMARY")
    if target:
        with open(target, "a", encoding="utf-8") as out:
            out.write(text + "\n\n")


def describe(packages: list[Package]) -> str:
    return f"**{packages[0].version}**, packed from commit `{packages[0].commit or 'unknown'}` ({len(packages)} packages)"


def cmd_plan(args: argparse.Namespace) -> int:
    packages = load_set(args.directory)
    check_against_projects(packages, args.projects)
    for package in packages:
        print(f"{package.level}\t{package.id}\t{package.version}")
    return 0


def cmd_push(args: argparse.Namespace) -> int:
    packages = load_set(args.directory)
    check_against_projects(packages, args.projects)
    if args.commit and packages[0].commit and packages[0].commit != args.commit:
        raise SetError(f"packed from {packages[0].commit}, but asked to publish {args.commit}")
    key = os.environ.get(args.api_key_env, "") if args.api_key_env else ""
    if args.api_key_env and not key:
        raise SetError(f"${args.api_key_env} is empty")

    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf-8") as out:
            out.write(f"version={packages[0].version}\ncommit={packages[0].commit}\n")
    summary(f"Publishing {describe(packages)} to `{args.source}`.")

    served: set[str] = set()
    # Level after level, never two at once: the order IS the guarantee.
    for number, level in enumerate(levels_of(packages)):
        if args.gate_index and number > 0:
            needed = [p for p in packages if p.key not in served and any(p.key in q.depends_on for q in level)]
            missing = wait_until_served(needed, args.gate_index, args.wait, args.poll, f"level {number}")
            if missing:
                summary(f"**Stopped before level {number}**: after {args.wait:g} s the feed still does not serve "
                        + ", ".join(f"`{p.id}`" for p in missing)
                        + ". Nothing that depends on them was pushed. Re-run this job to finish the version.")
                return 1
            served.update(p.key for p in needed)

        print(f"==> level {number}: {', '.join(p.id for p in level)}", flush=True)
        failed = push_all([p.path for p in level], args, key)
        if failed:
            summary(f"**Push failed at level {number}**: " + ", ".join(f"`{name}`" for name in failed)
                    + ". Nothing that depends on them was pushed. Re-run this job to finish the version.")
            return 1

    summary(f"Pushed {describe(packages)}, dependencies first.")
    return 0


def cmd_verify(args: argparse.Namespace) -> int:
    packages = load_set(args.directory)
    missing = wait_until_served(packages, args.index, args.wait, args.poll, "verify")
    if missing:
        summary(f"**Incomplete**: {describe(packages)} -- after {args.wait:g} s `{args.index}` still does not serve "
                + ", ".join(f"`{p.id}`" for p in missing) + ".")
        return 1
    summary(f"Complete: `{args.index}` serves {describe(packages)}.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.partition("\n")[0])
    commands = parser.add_subparsers(dest="command", required=True)

    plan = commands.add_parser("plan", help="print level, id and version in push order")
    plan.set_defaults(run=cmd_plan)

    push = commands.add_parser("push", help="push the set, dependencies first")
    push.add_argument("--source", required=True)
    push.add_argument("--api-key-env", help="the environment variable holding the key (never the key)")
    push.add_argument("--gate-index", help="nuget.org, or a URL with {id}: wait here for dependencies")
    push.add_argument("--log", help="append each package's accepted push output here")
    push.add_argument("--commit", help="the commit the set must have been packed from")
    push.add_argument("--timeout", type=int, default=900, help="seconds, per push request")
    push.add_argument("--attempts", type=int, default=5)
    push.add_argument("--backoff", type=float, default=15, help="seconds before the second attempt; doubles")
    push.add_argument("--throttle-wait", type=float, default=60, help="floor after a 429 with no Retry-After")
    push.add_argument("--max-wait", type=float, default=600, help="ceiling on any one retry wait")
    push.add_argument("--jobs", type=int, default=4, help="pushes at once inside one level")
    push.set_defaults(run=cmd_push)

    verify = commands.add_parser("verify", help="wait until the feed serves every package of the set")
    verify.add_argument("--index", default="nuget.org", help="nuget.org, or a URL with {id}")
    verify.set_defaults(run=cmd_verify)

    for command in (plan, push, verify):
        command.add_argument("directory", type=Path)
    for command in (plan, push):
        command.add_argument("--projects", type=Path, help="src: the set must be the packable projects under it")
    for command in (push, verify):
        command.add_argument("--wait", type=float, default=1800, help="seconds to wait for the feed")
        command.add_argument("--poll", type=float, default=15)

    args = parser.parse_args()
    try:
        return args.run(args)
    except SetError as problem:
        summary(f"**Not published**: {problem}.")
        return 2


if __name__ == "__main__":
    sys.exit(main())
