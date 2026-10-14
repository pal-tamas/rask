#!/usr/bin/env python3
"""Move RaskLitestreamVersion to Litestream's newest release, with a SHA-256 Rask computed for every asset.

    Rask.SQLite.Litestream.props     RaskLitestreamVersion, the binary a build downloads
    Rask.SQLite.Litestream.targets   one _RaskLsSha row per os-arch, which the build checks the download against
    template Dockerfiles             COPY --from=litestream/litestream:<version>, the same release

Each asset is downloaded and hashed here, and must agree with the release's checksums.txt: a digest is
only worth recording when two answers match. Releases come from GitHub (GH_TOKEN raises the rate limit).

Usage:  scripts/upstream/litestream.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape or the digests disagree
"""
import re
import sys

from upstream import ROOT, ShapeChanged, capture, checksums, fetch, fetch_json, follow, numbers, sha256, stable, substitute

PROPS = "src/Rask.SQLite.Litestream/build/Rask.SQLite.Litestream.props"
TARGETS = "src/Rask.SQLite.Litestream/build/Rask.SQLite.Litestream.targets"
RELEASES = "https://github.com/benbjohnson/litestream/releases/download"
ROW = re.compile(r"(<_RaskLsSha Condition=\"'\$\(RaskLitestreamVersion\)' == ')([\d.]+)(' and '\$\(_RaskLsOs\)-\$\(_RaskLsArch\)' == '([\w-]+)'\">)(\w+)(<)")


def step():
    stated = capture(PROPS, r"<RaskLitestreamVersion [^>]*>([\d.]+)<")
    release = fetch_json("https://api.github.com/repos/benbjohnson/litestream/releases/latest")
    latest = release["tag_name"].lstrip("v")
    moved = None
    if stable(latest) and numbers(latest) > numbers(stated):
        pin(latest)
        moved = f"{stated} -> {latest}"
    else:
        latest = stated

    # The images copy the binary the package downloads, whether or not the pin moved today.
    for dockerfile in sorted(ROOT.glob("src/Rask.Templates/*/Dockerfile")):
        if "litestream/litestream:" in dockerfile.read_text(encoding="utf-8"):
            substitute(str(dockerfile.relative_to(ROOT)), r"litestream/litestream:[\d.]+", f"litestream/litestream:{latest}")
    return moved


def pin(latest):
    """Record latest and the digest of each asset the targets name."""
    platforms = [m.group(4) for m in ROW.finditer((ROOT / TARGETS).read_text(encoding="utf-8"))]
    if not platforms:
        raise ShapeChanged(f"{TARGETS}: no _RaskLsSha rows")
    published = checksums(fetch(f"{RELEASES}/v{latest}/checksums.txt").decode())

    digests = {}
    for platform in platforms:
        asset = f"litestream-{latest}-{platform}{'.zip' if platform.startswith('windows') else '.tar.gz'}"
        digests[platform] = sha256(fetch(f"{RELEASES}/v{latest}/{asset}"))
        if published.get(asset) != digests[platform]:
            raise ShapeChanged(f"{asset}: downloaded {digests[platform]}, checksums.txt says {published.get(asset)}")

    substitute(PROPS, r"(<RaskLitestreamVersion [^>]*>)[\d.]+(<)", rf"\g<1>{latest}\g<2>")
    substitute(TARGETS, ROW, lambda m: f"{m.group(1)}{latest}{m.group(3)}{digests[m.group(4)]}{m.group(6)}")


if __name__ == "__main__":
    sys.exit(follow("litestream", step))
