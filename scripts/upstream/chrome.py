#!/usr/bin/env python3
"""Move Rask.Pdf's pinned Chrome for Testing headless shell to the newest Stable release at least 7 days old.

    src/Rask.Pdf/build/Rask.Pdf.ChromePin.props   RaskPdfChromeVersion and one RaskPdfChromeSha256_<platform>,
                                                  which the build checks each download against before unpacking

A week in Stable is the bar: a release that shipped yesterday may still be pulled or patched, and every app that
prints would fetch it at its next build. Release dates come from Chromium's own dashboard; the downloads from
Chrome for Testing's list of known-good versions. Google publishes no checksums for these archives, so the digest
recorded is of the archive downloaded here, from Google's storage over HTTPS — where the build fetches it later.

Usage:  scripts/upstream/chrome.py          (RASK_UPSTREAM_NOW=2026-10-21T00:00:00Z pretends it is another day)
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape or a platform went missing
"""
import os
import re
import sys
from datetime import datetime, timedelta, timezone

from upstream import ROOT, ShapeChanged, capture, fetch, fetch_json, follow, numbers, sha256, substitute

PIN = "src/Rask.Pdf/build/Rask.Pdf.ChromePin.props"
RELEASES = "https://chromiumdash.appspot.com/fetch_releases?channel=Stable&platform=Linux&num=30"
KNOWN = "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json"
MIN_AGE = timedelta(days=7)
ROW = re.compile(r"(<RaskPdfChromeSha256_(\w+)>)(\w+)(<)")


def now():
    stated = os.environ.get("RASK_UPSTREAM_NOW")
    return datetime.fromisoformat(stated.replace("Z", "+00:00")) if stated else datetime.now(timezone.utc)


def step():
    stated = capture(PIN, r"<RaskPdfChromeVersion>([\d.]+)<")
    keys = [m.group(2) for m in ROW.finditer((ROOT / PIN).read_text(encoding="utf-8"))]
    if not keys:
        raise ShapeChanged(f"{PIN}: no RaskPdfChromeSha256_ rows")

    cutoff = (now() - MIN_AGE).timestamp() * 1000
    aged = {r["version"] for r in fetch_json(RELEASES) if r.get("time", float("inf")) <= cutoff}
    known = {v["version"]: v for v in fetch_json(KNOWN)["versions"]}
    candidates = [v for v in aged if v in known and shells(known[v], keys)]
    if not candidates:
        return None
    latest = max(candidates, key=numbers)
    if numbers(latest) <= numbers(stated):
        return None

    urls = shells(known[latest], keys)
    digests = {key: sha256(fetch(urls[key])) for key in keys}
    substitute(PIN, r"(<RaskPdfChromeVersion>)[\d.]+(<)", rf"\g<1>{latest}\g<2>")
    substitute(PIN, ROW, lambda m: f"{m.group(1)}{digests[m.group(2)]}{m.group(4)}")
    return f"{stated} -> {latest}"


def shells(version, keys):
    """The headless shell's download per pinned platform, or None when a platform has none."""
    urls = {d["platform"]: d["url"] for d in version.get("downloads", {}).get("chrome-headless-shell", [])}
    found = {key: urls.get(key.replace("_", "-")) for key in keys}
    return found if all(found.values()) else None


if __name__ == "__main__":
    sys.exit(follow("chrome-for-testing", step))
