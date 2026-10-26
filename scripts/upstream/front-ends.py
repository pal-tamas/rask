#!/usr/bin/env python3
"""Refresh the npm releases `rask new` chooses front-end versions from when the registry does not answer.

Online, `rask new` reads every floating package's releases from registry.npmjs.org and writes the newest set
that installs together. Offline, behind a filtering proxy or past its 20-second budget, it reads this snapshot
instead (src/Rask.Cli/Scaffolding/npm.snapshot.json, NpmSnapshot.cs) and runs the same choice over it, so the
project still gets caret ranges that install together rather than a bare "latest" npm may refuse (ERESOLVE).

The packages are the ones the templates float ("latest" in a client package.json or an island fragment), plus
vite-plus, which the alias names. Each keeps the newest patch of every minor of its newest major, and the newest
release of the two majors before it (a peer range names majors), with
the peers that name another floating package (or vite) and its engines.node: all the choice reads. A vite-plus
release also records the vite its core bundles, which is what the plugins' vite peer is met by.

The date moves only when a release does, so a day with nothing new writes nothing.

Usage:  scripts/upstream/front-ends.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape
"""
import datetime
import json
import sys

from upstream import ROOT, ShapeChanged, fetch_json, follow, numbers, stable

NPM = "https://registry.npmjs.org"
ABBREVIATED = "application/vnd.npm.install-v1+json"
SNAPSHOT = "src/Rask.Cli/Scaffolding/npm.snapshot.json"
TOOLCHAIN = "vite-plus"
CORE = "@voidzero-dev/vite-plus-core"
ALIAS = f"npm:{CORE}@latest"
MAJORS = 3


def floating():
    """Every package a template or an island fragment leaves at "latest", and the toolchain the alias names."""
    manifests = sorted(ROOT.glob("src/Rask.Templates/*/client/package.json")) + sorted(
        ROOT.glob("src/Rask.Templates/_islands/*/island.json"))
    if not manifests:
        raise ShapeChanged("no template package.json or island.json under src/Rask.Templates")
    packages = set()
    for manifest in manifests:
        document = json.loads(manifest.read_text(encoding="utf-8"))
        for section in ("dependencies", "devDependencies"):
            for name, value in document.get(section, {}).items():
                if value == "latest":
                    packages.add(name)
                elif value == ALIAS:
                    packages.add(TOOLCHAIN)
    return sorted(packages)


def line(version):
    """What a caret holds a release to: the major, or the minor under 0.x."""
    major, minor = numbers(version)[:2]
    return (major, minor if major == 0 else 0)


def kept(document, packages):
    """Every minor's newest patch in the newest major, the newest of the majors before it: what the choice reads."""
    versions = {v: r for v, r in document.get("versions", {}).items() if stable(v) and "deprecated" not in r}
    lines = sorted({line(v) for v in versions}, reverse=True)[:MAJORS]
    newest = {}
    for version in versions:
        key = numbers(version)[:2] if line(version) == lines[0] else line(version)
        if line(version) in lines and numbers(version) > numbers(newest.get(key, "0.0.0")):
            newest[key] = version

    releases = {}
    for version in sorted(newest.values(), key=numbers, reverse=True):
        release = versions[version]
        entry = {}
        peers = {n: r for n, r in release.get("peerDependencies", {}).items() if n in packages or n == "vite"}
        if peers:
            entry["peerDependencies"] = dict(sorted(peers.items()))
        node = release.get("engines", {}).get("node") if isinstance(release.get("engines"), dict) else None
        if isinstance(node, str):
            entry["engines"] = {"node": node}
        releases[version] = entry
    return releases


def bundled_vite(version):
    return fetch_json(f"{NPM}/{CORE.replace('/', '%2f')}/{version}").get("bundledVersions", {}).get("vite")


def step():
    packages = floating()
    path = ROOT / SNAPSHOT
    stated = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {"packages": {}}

    snapshot = {}
    for package in packages:
        releases = kept(fetch_json(f"{NPM}/{package.replace('/', '%2f')}", ABBREVIATED), packages)
        if package == TOOLCHAIN:
            known = stated["packages"].get(TOOLCHAIN, {}).get("versions", {})
            for version, entry in releases.items():
                vite = known.get(version, {}).get("bundledVersions", {}).get("vite") or bundled_vite(version)
                if vite:
                    entry["bundledVersions"] = {"vite": vite}
        snapshot[package] = {"versions": releases}

    if snapshot == stated["packages"]:
        return None

    moved = [p for p in packages if snapshot[p] != stated["packages"].get(p)]
    gone = sorted(set(stated["packages"]) - set(packages))
    document = {"followed": datetime.date.today().isoformat(), "packages": snapshot}
    path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return ", ".join(moved + [f"-{p}" for p in gone])


if __name__ == "__main__":
    sys.exit(follow("front-end releases", step))
