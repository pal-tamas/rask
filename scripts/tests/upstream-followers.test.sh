#!/usr/bin/env bash
# gate-inputs: scripts/upstream/.*\.py$|src/Rask\.Cli/Commands/(DeployCommand|DbCommand\.Backup)\.cs$|src/Rask\.SQLite\.Litestream/build/|src/Rask\.Templates/[^/]+/(Dockerfile|client/package\.json)$|Rask\.Core\.targets$|Rask\.External\.props$|TypeScriptToolPins\.cs$|Rask\.Tailwind\.props$|TailwindPins\.cs$|docs/tailwind\.md$|Rask\.External\.Tests\.csproj$|GitHubActionsWorkflow\.cs$
# The Python followers under scripts/upstream/, each run in a copy of the files it rewrites against saved
# upstream answers (RASK_UPSTREAM_FIXTURES): a newer stable release moves every copy of the pin, with digests
# computed from what was downloaded; a prerelease, an older release or a mismatched checksum moves nothing.
#
# These edits land with nobody reading them. A copy one misses is a gate failure at best, and a digest that
# was never checked is a build that executes whatever the network handed it.
#
# Usage:  scripts/tests/upstream-followers.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-followers.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

files=(scripts/upstream/upstream.py scripts/upstream/images.py scripts/upstream/litestream.py scripts/upstream/tools.py
       scripts/upstream/fixtures.py scripts/upstream/actions.py
       src/Rask.Cli/Commands/DeployCommand.cs src/Rask.Cli/Commands/DbCommand.Backup.cs tests/Rask.Cli.Tests/DeployCommandTests.cs
       tests/Rask.Cli.E2E.Tests/DeployHostE2ETests.cs scripts/run-providers-local.sh src/Rask.Templates/wasm/Dockerfile
       src/Rask.SQLite.Litestream/build/Rask.SQLite.Litestream.props src/Rask.SQLite.Litestream/build/Rask.SQLite.Litestream.targets
       src/Rask.Templates/server/Dockerfile src/Rask.Templates/react/Dockerfile
       src/Rask.Core/build/Rask.Core.targets src/Rask.External/build/Rask.External.props src/Rask.TypeScript.Tasks/TypeScriptToolPins.cs
       src/Rask.Tailwind/build/Rask.Tailwind.props src/Rask.Tailwind.Tasks/TailwindPins.cs docs/tailwind.md
       src/Rask.Templates/react/client/package.json src/Rask.Templates/angular/client/package.json
       tests/Rask.External.Tests/Rask.External.Tests.csproj src/Rask.Cli/Scaffolding/GitHubActionsWorkflow.cs)
fresh() {
  rm -rf "$tmp/tree" "$tmp/saved"
  mkdir -p "$tmp/saved"
  for f in "${files[@]}"; do mkdir -p "$tmp/tree/$(dirname "$f")"; cp "$root/$f" "$tmp/tree/$f"; done
}
# `answer URL TEXT` saves what upstream says at URL, under the name the followers' fetch looks for.
answer() { printf '%s' "$2" > "$tmp/saved/$(cd "$root/scripts/upstream" && python3 -c 'import sys, upstream; print(upstream.fixture_name(sys.argv[1]))' "$1")"; }
run() { RASK_UPSTREAM_FIXTURES="$tmp/saved" python3 "$tmp/tree/scripts/upstream/$1" >"$tmp/out" 2>&1; }
sha256() { printf '%s' "$1" | shasum -a 256 | cut -d' ' -f1; }
sha512() { printf 'sha512-%s' "$(printf '%s' "$1" | openssl dgst -sha512 -binary | base64 | tr -d '\n')"; }

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}
count() { (grep -rF --exclude='*.py' -- "$1" "$tmp/tree" || true) | wc -l | tr -d ' '; }
unchanged() { for f in "$@"; do cmp -s "$tmp/tree/$f" "$root/$f" || echo "$f"; done; }
hub() { answer "https://hub.docker.com/v2/repositories/$1/tags?page_size=100&ordering=last_updated" "{\"results\":[$(printf '{"name":"%s"},' "${@:2}" | sed 's/,$//')]}"; }
release() { answer "https://api.github.com/repos/$1/releases/latest" "{\"tag_name\":\"$2\"}"; }

echo "==> upstream images"
curl_was="$(sed -n 's/.*CurlImage = "curlimages\/curl:\([0-9.]*\)".*/\1/p' "$root/src/Rask.Cli/Commands/DeployCommand.cs")"
alpine_was="$(sed -n 's/.*SqliteImage = "alpine:\([0-9.]*\)".*/\1/p' "$root/src/Rask.Cli/Commands/DbCommand.Backup.cs")"
fresh
hub curlimages/curl latest 99.1.0 99.0.3 99.2.0-rc1 7.0.0
hub library/alpine latest edge 99.30.1 99.30 99.31-rc 20990101
hub library/postgres 99-alpine 99.2-alpine 100beta1-alpine 99 latest
run images.py
check "curl moves to the newest x.y.z, prerelease skipped" 1 "$(count 'CurlImage = "curlimages/curl:99.1.0"')"
check "...and the tests that assert it move with it"   0 "$(count "curlimages/curl:$curl_was")"
check "alpine keeps its x.y shape"                     1 "$(count 'SqliteImage = "alpine:99.30"')"
check "...and no older alpine is left"                 0 "$(count "alpine:$alpine_was\"")"
check "...in the E2E's images too, qualified or not"   2 "$(grep -cF 'alpine:99.30' "$tmp/tree/tests/Rask.Cli.E2E.Tests/DeployHostE2ETests.cs")"
check "postgres takes the new major, still -alpine"    1 "$(count 'postgres:99-alpine')"
check "an image the follower does not own is left"     "" "$(unchanged src/Rask.Templates/wasm/Dockerfile)"

fresh
hub curlimages/curl 1.0.0
hub library/alpine 1.0
hub library/postgres 1-alpine
run images.py
check "an older tag moves nothing" "" "$(unchanged "${files[@]}")"

echo "==> upstream litestream"
platforms="$(sed -n "s/.*_RaskLsOs)-\$(_RaskLsArch)' == '\([a-z0-9_-]*\)'.*/\1/p" "$root/src/Rask.SQLite.Litestream/build/Rask.SQLite.Litestream.targets")"
litestream_release() {
  release benbjohnson/litestream v99.0.1
  sums=""
  for p in $platforms; do
    ext=.tar.gz; case "$p" in windows-*) ext=.zip ;; esac
    answer "https://github.com/benbjohnson/litestream/releases/download/v99.0.1/litestream-99.0.1-$p$ext" "binary $p"
    sums="$sums$(sha256 "${1:-binary} $p")  litestream-99.0.1-$p$ext"$'\n'
  done
  answer "https://github.com/benbjohnson/litestream/releases/download/v99.0.1/checksums.txt" "$sums"
}
fresh
litestream_release
run litestream.py
check "the props name the new release"                1 "$(count '>99.0.1</RaskLitestreamVersion>')"
check "every platform row is for it"                  "$(echo "$platforms" | wc -l | tr -d ' ')" "$(count "== '99.0.1' and")"
check "...with the digest of what was downloaded"     1 "$(count "'linux-x86_64'\">$(sha256 'binary linux-x86_64')<")"
check "the template images copy the same release"     2 "$(count 'litestream/litestream:99.0.1')"

fresh
litestream_release tampered
check "a digest that disagrees with checksums.txt..." refused "$(run litestream.py || echo refused)"
check "...moves nothing"                               "" "$(unchanged "${files[@]}")"

echo "==> upstream tools"
fresh
answer "https://registry.npmjs.org/esbuild" '{"dist-tags":{"latest":"99.0.0"},"versions":{}}'
answer "https://registry.npmjs.org/typescript" '{"dist-tags":{"latest":"98.1.0","next":"99.0.0-dev.1"},"versions":{"6.0.3":{},"6.9.9":{},"6.10.0-beta":{},"98.1.0":{}}}'
pins="$tmp/tree/src/Rask.TypeScript.Tasks/TypeScriptToolPins.cs"
while read -r package; do
  case "$package" in @esbuild/*) v=99.0.0 ;; typescript) v=6.9.9 ;; *) v=98.1.0 ;; esac
  answer "https://registry.npmjs.org/${package//\//%2f}/$v" "{\"dist\":{\"integrity\":\"$(sha512 "tgz $package")\"}}"
  answer "https://registry.npmjs.org/$package/-/${package##*/}-$v.tgz" "tgz $package"
done < <(sed -n 's/.*\["\(.*\)@[^@]*"\] = .*/\1/p' "$pins")
release tailwindlabs/tailwindcss v99.0.0
sums=""
for asset in $(sed -n 's/.*\["\(tailwindcss-[^"]*\)"\].*/\1/p' "$tmp/tree/src/Rask.Tailwind.Tasks/TailwindPins.cs"); do
  answer "https://github.com/tailwindlabs/tailwindcss/releases/download/v99.0.0/$asset" "cli $asset"
  sums="$sums$(sha256 "cli $asset")  ./$asset"$'\n'
done
answer "https://github.com/tailwindlabs/tailwindcss/releases/download/v99.0.0/sha256sums.txt" "$sums"
run tools.py
check "esbuild is npm's latest"                        1 "$(count '>99.0.0</RaskEsbuildVersion>')"
check "...with a row per platform package"             9 "$(count '@99.0.0"] = ')"
check "...holding the downloaded tarball's sha512"     1 "$(count "[\"@esbuild/linux-x64@99.0.0\"] = \"$(sha512 'tgz @esbuild/linux-x64')\"")"
check "tsgo is typescript's latest, prerelease skipped" 1 "$(count '>98.1.0</RaskTsgoVersion>')"
check "...from the stable native packages"             6 "$(count '["@typescript/typescript-')"
check "the props extractor stays on the newest 6.x"    1 "$(count '>6.9.9</RaskExternalTypeScriptVersion>')"
check "...with its row"                                1 "$(count '["typescript@6.9.9"]')"
check "Tailwind's pin, table and docs move together"   3 "$(( $(count '>99.0.0</RaskTailwindVersion>') + $(count 'Version = "99.0.0"') + $(count '| `99.0.0` |') ))"
check "every Tailwind asset holds its own digest"      1 "$(count "[\"tailwindcss-linux-x64\"] = \"$(sha256 'cli tailwindcss-linux-x64')\"")"
check "a new major moves the templates' ranges"        4 "$(count '"^99.0.0"')"

fresh
answer "https://registry.npmjs.org/esbuild" '{"dist-tags":{"latest":"0.0.1"},"versions":{}}'
answer "https://registry.npmjs.org/typescript" '{"dist-tags":{"latest":"99.0.0-rc"},"versions":{"5.9.9":{},"99.0.0-rc":{}}}'
release tailwindlabs/tailwindcss v1.0.0
run tools.py
check "an older or prerelease compiler moves nothing"  "" "$(unchanged "${files[@]}")"

echo "==> upstream fixtures"
fresh
for package in preact happy-dom react vue solid-js svelte; do
  answer "https://registry.npmjs.org/$package" '{"dist-tags":{"latest":"99.0.0"},"versions":{}}'
done
answer "https://registry.npmjs.org/@angular%2fcore" '{"dist-tags":{"latest":"99.1.0"},"versions":{"99.1.0":{"peerDependencies":{"rxjs":"^7.4.0 || ^98.0.0"}}}}'
answer "https://registry.npmjs.org/rxjs" '{"dist-tags":{"latest":"99.0.0"},"versions":{"7.8.2":{},"98.1.0":{},"98.2.0-beta.1":{},"99.0.0":{}}}'
run fixtures.py
check "every framework is npm's latest"                6 "$(count '>99.0.0</Rask')"
check "Angular is one number for its four packages"    1 "$(count '>99.1.0</RaskAngularFixtureVersion>')"
check "rxjs is the newest Angular's peer range takes"  1 "$(count '>98.1.0</RaskRxjsFixtureVersion>')"

echo "==> upstream actions"
fresh
release actions/checkout v99.1.0
release actions/setup-dotnet v1.0.0
run actions.py
check "a newer major moves the scaffolded workflow"    1 "$(count 'actions/checkout@v99')"
workflow=src/Rask.Cli/Scaffolding/GitHubActionsWorkflow.cs
check "an older one leaves it" "$(grep -o 'setup-dotnet@v[0-9]*' "$root/$workflow")" "$(grep -o 'setup-dotnet@v[0-9]*' "$tmp/tree/$workflow")"

[ "$failures" -eq 0 ] || { echo "upstream followers: $failures failed" >&2; exit 1; }
echo "upstream followers: ok"
