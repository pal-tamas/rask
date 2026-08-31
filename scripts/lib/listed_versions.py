#!/usr/bin/env python3
"""Print the versions of a package that are currently LISTED on nuget.org.

Why not the flat-container index (`v3-flatcontainer/<id>/index.json`), which is the obvious source and
one line shorter: it reports every version ever pushed, listed or not. Feeding it to the unlister makes
each release re-attempt every version it already unlisted -- and since nuget.org's unlist quota is
~250 calls, the budget is spent entirely on no-ops and the backlog never moves. Measured: rask.native
had all 209 versions unlisted and flat-container still returned all 209.

The registration index carries `listed` per version, so it is the one that answers "what is left to do".

Usage:  listed_versions.py <package-id>          fetch from nuget.org
        listed_versions.py --parse < reg.json    parse a registration index on stdin (for tests)
"""
import gzip
import json
import sys
import urllib.request


def fetch(url):
    # The registration5-gz-* endpoints serve gzip whatever the request asks for.
    req = urllib.request.Request(url, headers={"Accept-Encoding": "gzip"})
    with urllib.request.urlopen(req) as r:
        raw = r.read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.decompress(raw)
    return json.loads(raw)


def listed(index, resolve=fetch):
    """Versions with listed != false. A page may inline its items or link them by @id."""
    out = []
    for page in index.get("items", []):
        items = page.get("items")
        if items is None:
            items = resolve(page["@id"])["items"]
        for it in items:
            entry = it["catalogEntry"]
            # Absent `listed` means listed: that is the nuget.org default for older catalog entries.
            if entry.get("listed", True):
                out.append(entry["version"])
    return out


def main():
    if len(sys.argv) != 2:
        print("usage: listed_versions.py <package-id> | --parse", file=sys.stderr)
        return 2
    if sys.argv[1] == "--parse":
        index = json.load(sys.stdin)
    else:
        pid = sys.argv[1].lower()
        try:
            index = fetch(f"https://api.nuget.org/v3/registration5-gz-semver2/{pid}/index.json")
        except Exception as exc:                                 # noqa: BLE001
            # Not on nuget.org, or the feed is unreachable. The caller treats "no versions" as
            # "nothing to do", which is the right answer for a cleanup step that must not fail.
            print(f"listed_versions: {pid}: {exc}", file=sys.stderr)
            return 0
    for v in listed(index):
        print(v)
    return 0


if __name__ == "__main__":
    sys.exit(main())
