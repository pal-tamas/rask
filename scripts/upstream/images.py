#!/usr/bin/env python3
"""Move every Docker image tag the repo names in code to the newest stable tag of the same shape.

    curlimages/curl   DeployCommand.CurlImage, the health probe, and the tests that assert it
    alpine            DbCommand.Backup's SqliteImage, and the deploy-host E2E's images
    postgres          the provider and bulk-insert scripts' throwaway server (NN-alpine: the major moves too)

A tag keeps its shape: 8.11.1 moves to the newest x.y.z, 3.21 to the newest x.y, 17-alpine to the newest
NN-alpine, so a pin never turns into a floating one or a prerelease. Tags come from Docker Hub.
The template Dockerfiles' FROM lines are Dependabot's (.github/dependabot.yml), not this.

Usage:  scripts/upstream/images.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape
"""
import os
import re
import subprocess
import sys

from upstream import ROOT, fetch_json, follow, numbers

IMAGES = {
    "curlimages/curl": "curlimages/curl",
    "alpine": "library/alpine",
    "postgres": "library/postgres",
}
SCANNED = re.compile(r"(\.(cs|sh|ps1|ya?ml)|/Dockerfile)$")
# .github is Dependabot's, and the workflow token may not push a change to a workflow anyway.
PRUNED = {".git", ".github", ".claude", "bin", "obj", "node_modules", "artifacts", "TestResults"}


def files():
    if (ROOT / ".git").exists():
        listed = subprocess.run(["git", "-C", str(ROOT), "ls-files"], capture_output=True, text=True, check=True)
        return [p for p in listed.stdout.splitlines() if SCANNED.search("/" + p) and p.split("/")[0] not in PRUNED]
    found = []
    for folder, dirs, names in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in PRUNED]
        found += [os.path.relpath(os.path.join(folder, n), ROOT) for n in names]
    return [p for p in found if SCANNED.search("/" + p)]


def reference(image):
    official = "" if "/" in image else r"(?:docker\.io/)?(?:library/)?"
    return re.compile(r"(?<![\w./-])(" + official + re.escape(image) + r"):([0-9][\w.-]*)")


def newest_like(tag, tags):
    shape = re.compile(re.sub(r"\d+", r"\\d+", re.escape(tag)) + r"\Z")
    same = [t for t in tags if shape.match(t)]
    return max(same, key=numbers) if same else tag


def step():
    paths = files()
    texts = {p: (ROOT / p).read_text(encoding="utf-8") for p in paths}
    moved = []
    for image, repository in IMAGES.items():
        pattern = reference(image)
        stated = {m.group(2) for text in texts.values() for m in pattern.finditer(text)}
        if not stated:
            continue
        hub = fetch_json(f"https://hub.docker.com/v2/repositories/{repository}/tags?page_size=100&ordering=last_updated")
        tags = [t["name"] for t in hub["results"]]
        bumps = {old: new for old in stated if numbers(new := newest_like(old, tags)) > numbers(old)}
        if not bumps:
            continue
        for path, text in texts.items():
            changed = pattern.sub(lambda m: f"{m.group(1)}:{bumps.get(m.group(2), m.group(2))}", text)
            if changed != text:
                texts[path] = changed
                (ROOT / path).write_text(changed, encoding="utf-8")
        moved += [f"{image} {old} -> {new}" for old, new in sorted(bumps.items())]
    return ", ".join(moved)


if __name__ == "__main__":
    sys.exit(follow("images", step))
