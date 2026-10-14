#!/usr/bin/env python3
"""Move the adapter fixtures' framework versions (tests/Rask.External.Tests) to each package's npm latest.

The fixtures are the adapters' only coverage below a browser, and an app scaffolded today gets each
framework's latest, so that is what they test. rxjs is the one peer that matters: it takes the newest
release that the new @angular/core accepts. A framework release that breaks an adapter fails that
project's tests in the gates, which is the point.

Usage:  scripts/upstream/fixtures.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape
"""
import re
import sys

from upstream import capture, fetch_json, follow, numbers, stable, substitute

PROJECT = "tests/Rask.External.Tests/Rask.External.Tests.csproj"
NPM = "https://registry.npmjs.org"
# property -> the package whose npm latest it follows (React's number is react-dom's too, Angular's every @angular/*)
LATEST = {
    "RaskPreactFixtureVersion": "preact",
    "RaskHappyDomFixtureVersion": "happy-dom",
    "RaskReactFixtureVersion": "react",
    "RaskVueFixtureVersion": "vue",
    "RaskSolidFixtureVersion": "solid-js",
    "RaskSvelteFixtureVersion": "svelte",
    "RaskAngularFixtureVersion": "@angular/core",
}


def packument(package):
    return fetch_json(f"{NPM}/{package.replace('/', '%2f')}")


def satisfies(version, accepted):
    """npm's range grammar, as far as peerDependencies use it: `||` of space-joined ^ ~ >= > <= < = comparators."""
    v = numbers(version)
    for alternative in accepted.split("||"):
        if all(comparator_holds(v, c) for c in alternative.split()):
            return True
    return False


def comparator_holds(v, comparator):
    op, bound = re.fullmatch(r"(\^|~|>=|<=|>|<|=)?v?([\d.x*]+)", comparator).groups()
    parts = [int(p) for p in re.findall(r"\d+", bound)]
    floor = tuple(parts + [0] * (3 - len(parts)))
    if op == "^":
        lead = next((i for i, p in enumerate(floor) if p), 2)
        return v >= floor and v[:lead + 1] == floor[:lead + 1]
    if op == "~":
        return v >= floor and v[:2] == floor[:2]
    return {">=": v >= floor, "<=": v <= floor, ">": v > floor, "<": v < floor}.get(op, v[:len(parts)] == floor[:len(parts)])


def step():
    moved = []
    wanted = {}
    for prop, package in LATEST.items():
        document = packument(package)
        wanted[prop] = document["dist-tags"]["latest"]
        if package == "@angular/core":
            peer = document["versions"][wanted[prop]].get("peerDependencies", {}).get("rxjs", "*")
            rxjs = [v for v in packument("rxjs")["versions"] if stable(v) and (peer == "*" or satisfies(v, peer))]
            wanted["RaskRxjsFixtureVersion"] = max(rxjs, key=numbers)

    for prop, latest in wanted.items():
        stated = capture(PROJECT, rf"<{prop} [^>]*>([^<]+)<")
        if stable(latest) and numbers(latest) > numbers(stated):
            substitute(PROJECT, rf"(<{prop} [^>]*>)[^<]+(<)", rf"\g<1>{latest}\g<2>")
            moved.append(f"{prop.removeprefix('Rask').removesuffix('FixtureVersion')} {stated} -> {latest}")
    return ", ".join(moved)


if __name__ == "__main__":
    sys.exit(follow("adapter fixtures", step))
