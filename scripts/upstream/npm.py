"""npm's registry, read the way the followers read it: one release's manifest, its tarball refused unless it hashes to
the registry's own integrity, and the tarball's files read in memory. Nothing is installed and no package script
can run: a follower only ever reads a package's data.

RASK_UPSTREAM_FIXTURES answers every URL from saved files (upstream.fetch), so a self-test needs no network.
"""
import gzip
import io
import re
import tarfile

from upstream import ShapeChanged, fetch, fetch_json, sha512_integrity

REGISTRY = "https://registry.npmjs.org"


def release(name, version="latest"):
    """One release of `name` (a version, or a dist-tag: its stable `latest` by default), as the registry describes it."""
    manifest = fetch_json(f"{REGISTRY}/{name.replace('/', '%2f')}/{version}")
    if not re.fullmatch(r"\d+\.\d+\.\d+", manifest.get("version") or ""):
        raise ShapeChanged(f"npm's {version} {name} is {manifest.get('version')}, not a stable release")
    dist = manifest.get("dist") or {}
    if not dist.get("tarball") or not (dist.get("integrity") or "").startswith("sha512-"):
        raise ShapeChanged(f"{name} {manifest['version']} names no sha512 tarball")
    return manifest


def tarball(manifest):
    """The release's tarball, gzipped; refused unless it hashes to the integrity the registry names."""
    data = fetch(manifest["dist"]["tarball"])
    digest = sha512_integrity(data)
    if digest != manifest["dist"]["integrity"]:
        raise ShapeChanged(f"{manifest['dist']['tarball']} is not the tarball the registry names ({digest})")
    return data


def files(data, wanted=lambda path: True):
    """The regular files of a gzipped tarball that `wanted` takes, as {path: bytes}, paths as packed (`package/...`)."""
    out = {}
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(data)), mode="r:") as archive:
        for member in archive:
            if member.isfile() and wanted(member.name):
                out[member.name] = archive.extractfile(member).read()
    return out
