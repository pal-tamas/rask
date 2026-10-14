"""What the Python followers share: one fetch, the version order, and a rewrite that must find its target.

RASK_UPSTREAM_FIXTURES names a directory of saved answers, one file per URL (fixture_name), which is how
the self-tests under scripts/tests/ run a follower with no network.
"""
import base64
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UNREACHABLE = (urllib.error.URLError, TimeoutError, ConnectionError)


class ShapeChanged(Exception):
    """The repository no longer looks the way the follower expects: a person has to look."""


def fixture_name(url):
    return re.sub(r"[^A-Za-z0-9.-]+", "_", url.split("://", 1)[-1])


def fetch(url):
    saved = os.environ.get("RASK_UPSTREAM_FIXTURES")
    if saved:
        return (Path(saved) / fixture_name(url)).read_bytes()
    headers = {"User-Agent": "rask-upstream"}
    token = os.environ.get("GH_TOKEN")
    if token and url.startswith("https://api.github.com/"):
        headers["Authorization"] = "Bearer " + token
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=300) as response:
        return response.read()


def fetch_json(url):
    return json.loads(fetch(url))


def numbers(version):
    return tuple(int(n) for n in re.findall(r"\d+", version))


def stable(version):
    return re.fullmatch(r"\d+(\.\d+)*", version) is not None


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def sha512_integrity(data):
    return "sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode()


def checksums(text):
    """A sha256sum listing (`<digest>  ./<name>`) as {name: digest}."""
    rows = (line.split() for line in text.splitlines() if line.strip())
    return {name.lstrip("*").removeprefix("./"): digest for digest, name in rows}


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def capture(path, pattern):
    """The first group of pattern in path; ShapeChanged when it is not there."""
    match = re.search(pattern, read(path))
    if not match:
        raise ShapeChanged(f"{path}: nothing matches {pattern}")
    return match.group(1)


def substitute(path, pattern, replacement):
    """re.sub over one file. ShapeChanged when pattern matches nothing: a rewrite that misses is a stale pin."""
    text = read(path)
    changed, count = re.subn(pattern, replacement, text)
    if count == 0:
        raise ShapeChanged(f"{path}: nothing matches {pattern}")
    if changed != text:
        (ROOT / path).write_text(changed, encoding="utf-8")


def follow(name, step):
    """Run one follower: step() returns what moved, or None. Upstream not answering leaves the pin alone."""
    try:
        moved = step()
    except UNREACHABLE as error:
        print(f"{name}: upstream did not answer ({error}); leaving the pin as it is.", file=sys.stderr)
        return 0
    except ShapeChanged as error:
        print(f"{name}: {error}", file=sys.stderr)
        return 1
    print(f"{name}: {moved or 'already the newest'}.")
    return 0
