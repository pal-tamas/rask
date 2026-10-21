#!/usr/bin/env python3
"""Move Rask.Pdf's pinned Chrome for Testing headless shell to the Stable channel, with a SHA-256 per platform.

    src/Rask.Pdf/build/Rask.Pdf.ChromePin.props   RaskPdfChromeVersion and one RaskPdfChromeSha256_<platform>,
                                                  which the build checks each download against before unpacking

Google publishes no checksums for these archives, so the digest recorded is of the archive downloaded here,
from Google's own storage over HTTPS — the one place the build will later fetch the same bytes from.

Usage:  scripts/upstream/chrome.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape or a platform went missing
"""
import re
import sys

from upstream import ROOT, ShapeChanged, capture, fetch, fetch_json, follow, numbers, sha256, substitute

PIN = "src/Rask.Pdf/build/Rask.Pdf.ChromePin.props"
CHANNELS = "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json"
ROW = re.compile(r"(<RaskPdfChromeSha256_(\w+)>)(\w+)(<)")


def step():
    stated = capture(PIN, r"<RaskPdfChromeVersion>([\d.]+)<")
    stable = fetch_json(CHANNELS)["channels"]["Stable"]
    latest = stable["version"]
    if numbers(latest) <= numbers(stated):
        return None

    urls = {d["platform"]: d["url"] for d in stable["downloads"].get("chrome-headless-shell", [])}
    keys = [m.group(2) for m in ROW.finditer((ROOT / PIN).read_text(encoding="utf-8"))]
    if not keys:
        raise ShapeChanged(f"{PIN}: no RaskPdfChromeSha256_ rows")

    digests = {}
    for key in keys:
        platform = key.replace("_", "-")
        if platform not in urls:
            raise ShapeChanged(f"Chrome for Testing {latest} has no headless shell for {platform}")
        digests[key] = sha256(fetch(urls[platform]))

    substitute(PIN, r"(<RaskPdfChromeVersion>)[\d.]+(<)", rf"\g<1>{latest}\g<2>")
    substitute(PIN, ROW, lambda m: f"{m.group(1)}{digests[m.group(2)]}{m.group(4)}")
    return f"{stated} -> {latest}"


if __name__ == "__main__":
    sys.exit(follow("chrome-for-testing", step))
