#!/usr/bin/env python3
"""Move the compilers a Rask build downloads to their newest stable release, with digests Rask computed.

    esbuild      RaskEsbuildVersion (Rask.Core.targets)              npm esbuild, latest
    tsgo         RaskTsgoVersion (Rask.Core.targets)                 npm typescript, latest (7.x is native)
    typescript   RaskExternalTypeScriptVersion (Rask.External.props) npm typescript, newest 6.x: the props
                 extractor needs the JavaScript API, which 7.x does not ship
    Tailwind     RaskTailwindVersion (Rask.Tailwind.props)           GitHub tailwindlabs/tailwindcss, latest

Every package or asset the pin tables list (TypeScriptToolPins.cs, TailwindPins.cs) is downloaded and hashed
here, and must agree with what its registry publishes beside it. On a new Tailwind major the templates'
tailwindcss and @tailwindcss/* ranges move to ^<new> too, so they keep accepting the pin (TailwindVersionPinTests).

Usage:  scripts/upstream/tools.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape or a digest disagrees
"""
import re
import sys

from upstream import (ROOT, ShapeChanged, capture, checksums, fetch, fetch_json, follow, numbers, sha256,
                      sha512_integrity, stable, substitute)

NPM = "https://registry.npmjs.org"
CORE = "src/Rask.Core/build/Rask.Core.targets"
EXTERNAL = "src/Rask.External/build/Rask.External.props"
NPM_PINS = "src/Rask.TypeScript.Tasks/TypeScriptToolPins.cs"
TAILWIND = "src/Rask.Tailwind/build/Rask.Tailwind.props"
TAILWIND_PINS = "src/Rask.Tailwind.Tasks/TailwindPins.cs"
TAILWIND_RELEASES = "https://github.com/tailwindlabs/tailwindcss/releases/download"

# (name, file, property, package whose release it follows, the majors it may take, the pin rows' package names)
NPM_TOOLS = [
    ("esbuild", CORE, "RaskEsbuildVersion", "esbuild", None, r"@esbuild/[\w-]+"),
    ("tsgo", CORE, "RaskTsgoVersion", "typescript", None, r"@typescript/typescript-[\w-]+"),
    ("typescript", EXTERNAL, "RaskExternalTypeScriptVersion", "typescript", 6, r"typescript"),
]


def newest_npm(package, major):
    document = fetch_json(f"{NPM}/{package.replace('/', '%2f')}")
    if major is None:
        return document["dist-tags"]["latest"]
    versions = [v for v in document["versions"] if stable(v) and numbers(v)[0] == major]
    return max(versions, key=numbers) if versions else None


def tarball(package, version):
    """The tarball URL, derived the way TypeScriptTools.TarballUrl derives it."""
    return f"{NPM}/{package}/-/{package.split('/')[-1]}-{version}.tgz"


def follow_npm(name, path, prop, package, major, rows):
    stated = capture(path, rf"<{prop} [^>]*>([^<]+)<")
    latest = newest_npm(package, major)
    if not latest or not stable(latest) or numbers(latest) <= numbers(stated):
        return None

    row = re.compile(rf'\["({rows})@{re.escape(stated)}"\] = "(sha512-[^"]+)"')
    packages = [m.group(1) for m in row.finditer((ROOT / NPM_PINS).read_text(encoding="utf-8"))]
    if not packages:
        raise ShapeChanged(f"{NPM_PINS}: no rows for {name} {stated}")
    integrity = {}
    for each in packages:
        published = fetch_json(f"{NPM}/{each.replace('/', '%2f')}/{latest}")["dist"]["integrity"]
        integrity[each] = sha512_integrity(fetch(tarball(each, latest)))
        if integrity[each] != published:
            raise ShapeChanged(f"{each}@{latest}: downloaded {integrity[each]}, the registry says {published}")

    substitute(path, rf"(<{prop} [^>]*>)[^<]+(<)", rf"\g<1>{latest}\g<2>")
    substitute(NPM_PINS, row, lambda m: f'["{m.group(1)}@{latest}"] = "{integrity[m.group(1)]}"')
    return f"{name} {stated} -> {latest}"


def follow_tailwind():
    stated = capture(TAILWIND, r"<RaskTailwindVersion [^>]*>([\d.]+)<")
    latest = fetch_json("https://api.github.com/repos/tailwindlabs/tailwindcss/releases/latest")["tag_name"].lstrip("v")
    if not stable(latest) or numbers(latest) <= numbers(stated):
        return None

    row = re.compile(r'\["(tailwindcss-[\w.-]+)"\] = "(\w+)"')
    assets = [m.group(1) for m in row.finditer((ROOT / TAILWIND_PINS).read_text(encoding="utf-8"))]
    if not assets:
        raise ShapeChanged(f"{TAILWIND_PINS}: no asset rows")
    published = checksums(fetch(f"{TAILWIND_RELEASES}/v{latest}/sha256sums.txt").decode())
    digests = {}
    for asset in assets:
        digests[asset] = sha256(fetch(f"{TAILWIND_RELEASES}/v{latest}/{asset}"))
        if digests[asset] != published.get(asset):
            raise ShapeChanged(f"{asset}: downloaded {digests[asset]}, sha256sums.txt says {published.get(asset)}")

    substitute(TAILWIND, r"(<RaskTailwindVersion [^>]*>)[\d.]+(<)", rf"\g<1>{latest}\g<2>")
    substitute(TAILWIND_PINS, r'(Version = ")[\d.]+(")', rf"\g<1>{latest}\g<2>")
    substitute(TAILWIND_PINS, row, lambda m: f'["{m.group(1)}"] = "{digests[m.group(1)]}"')
    substitute("docs/tailwind.md", r"(\| `RaskTailwindVersion` \| `)[\d.]+(`)", rf"\g<1>{latest}\g<2>")
    if numbers(latest)[0] != numbers(stated)[0]:
        manifests = [*ROOT.glob("src/Rask.Templates/*/client/package.json"), *ROOT.glob("src/Rask.Templates/_islands/*/island.json")]
        for manifest in manifests:
            text = manifest.read_text(encoding="utf-8")
            moved = re.sub(r'("(?:tailwindcss|@tailwindcss/[\w-]+)"\s*:\s*)"\^[\d.]+"', rf'\g<1>"^{latest}"', text)
            if moved != text:
                manifest.write_text(moved, encoding="utf-8")
    return f"tailwind {stated} -> {latest}"


def step():
    # Each tool downloads and checks everything before it writes: one that fails half way leaves its pin as it was.
    moved = [follow_npm(*tool) for tool in NPM_TOOLS] + [follow_tailwind()]
    return ", ".join(m for m in moved if m)


if __name__ == "__main__":
    sys.exit(follow("tools", step))
