#!/usr/bin/env bash
# gate-inputs: src/Rask\.Ui/(Ui\.IconName\.cs|UiIconPaths\.g\.cs|UiFlagRegistry\.g\.cs|Flags/.*|Resources/(editor|phone)/package(-lock)?\.json|Resources/ui-(editor|phone).*\.js)$
# The node followers of Rask.Ui's upstreams, each run in a copy of the files it writes against saved npm answers
# (RASK_UPSTREAM_FIXTURES, scripts/upstream/npm.mjs): a new release is written with its version and integrity,
# and records the public surface when it moves it; a tarball that is not the one the registry names, or a
# registry that does not answer, moves nothing. The kit's engines run with a stand-in npm and build: a bundle
# that grows stops the run, one that does not lands.
#
# These edits land with nobody reading them, so what is checked here is what the daily run trusts.
#
# Usage:  scripts/tests/upstream-node-followers.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-node-followers.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

files=(scripts/upstream/npm.mjs scripts/flux/lib.mjs scripts/flux/icons.mjs scripts/flux/flags.mjs
       src/Rask.Ui/Ui.IconName.cs src/Rask.Ui/UiIconPaths.g.cs src/Rask.Ui/UiFlagRegistry.g.cs)
fresh() {
  rm -rf "$tmp/tree" "$tmp/saved" "$tmp/package"
  mkdir -p "$tmp/saved" "$tmp/tree/scripts/public-api"
  for f in "${files[@]}"; do mkdir -p "$tmp/tree/$(dirname "$f")"; cp "$root/$f" "$tmp/tree/$f"; done
  cp -R "$root/src/Rask.Ui/Flags" "$tmp/tree/src/Rask.Ui/Flags"
  # What the follower asks to record, in place of a build of the kit.
  printf 'import sys\nopen(sys.argv[0] + ".log", "a").write(" ".join(sys.argv[1:]) + "\\n")\n' > "$tmp/tree/scripts/public-api/record.py"
}
fixture() { node -e 'import(process.argv[1]).then(m => console.log(m.fixtureName(process.argv[2])))' "$root/scripts/upstream/npm.mjs" "$1"; }
integrity() { printf 'sha512-%s' "$(openssl dgst -sha512 -binary < "$1" | base64 | tr -d '\n')"; }
# `put PATH TEXT` adds a file to the package being made.
put() { mkdir -p "$(dirname "$tmp/$1")"; printf "%s" "$2" > "$tmp/$1"; }
# `publish NAME VERSION [INTEGRITY]` packs what put made as npm's latest NAME, naming INTEGRITY if given.
publish() {
  local tgz="$tmp/$1-$2.tgz" url="https://registry.npmjs.org/$1/-/$1-$2.tgz"
  (cd "$tmp" && COPYFILE_DISABLE=1 tar --format=ustar -czf "$tgz" package)
  cp "$tgz" "$tmp/saved/$(fixture "$url")"
  printf '{"name":"%s","version":"%s","dist":{"tarball":"%s","integrity":"%s"}}' "$1" "$2" "$url" "${3:-$(integrity "$tgz")}" \
    > "$tmp/saved/$(fixture "https://registry.npmjs.org/$1/latest")"
  rm -rf "$tmp/package"
}
run() { (cd "$tmp/tree" && RASK_UPSTREAM_FIXTURES="$tmp/saved" node "scripts/$1" "${@:2}") >"$tmp/out" 2>&1 || { cat "$tmp/out" >&2; return 1; }; }

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}
unchanged() { for f in "$@"; do cmp -s "$tmp/tree/$f" "$root/$f" || echo "$f"; done; }
has() { grep -cF -- "$2" "$tmp/tree/$1" || true; }
recorded() { cat "$tmp/tree/scripts/public-api/record.py.log" 2>/dev/null || true; }

echo "==> heroicons"
# `heroicons NAME D` puts icon NAME, drawn as path D, in all four folders.
heroicons() {
  local svg='xmlns="http://www.w3.org/2000/svg"' marks='aria-hidden="true" data-slot="icon"'
  put "package/24/outline/$1.svg" "<svg $svg fill=\"none\" viewBox=\"0 0 24 24\" stroke-width=\"1.5\" stroke=\"currentColor\" $marks><path stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"$2\"/></svg>"
  for folder in 24/solid:24 20/solid:20 16/solid:16; do
    put "package/${folder%:*}/$1.svg" "<svg $svg viewBox=\"0 0 ${folder#*:} ${folder#*:}\" fill=\"currentColor\" $marks><path d=\"$2\"/></svg>"
  done
}
icons=(src/Rask.Ui/Ui.IconName.cs src/Rask.Ui/UiIconPaths.g.cs)

fresh
heroicons alpha 'M1 2h3'; heroicons beta-gamma 'M4 5v6'
publish heroicons 99.0.0
run flux/icons.mjs
check "a new release is written"                       1 "$(has src/Rask.Ui/Ui.IconName.cs '        BetaGamma,')"
check "...naming its version and integrity"            2 "$(grep -l "heroicons 99.0.0" "${icons[@]/#/$tmp/tree/}" | xargs grep -lF "$(sed -n 's/.*"integrity":"\([^"]*\)".*/\1/p' "$tmp/saved/$(fixture https://registry.npmjs.org/heroicons/latest)")" | wc -l | tr -d ' ')"
check "...and the icons it moved are recorded"         "src/Rask.Ui" "$(recorded)"
check "--check passes on what it wrote"                passed "$(run flux/icons.mjs --check && echo passed)"

heroicons alpha 'M1 2h4'; heroicons beta-gamma 'M4 5v6'
publish heroicons 99.0.1
rm -f "$tmp/tree/scripts/public-api/record.py.log"
run flux/icons.mjs
check "a release that only redraws moves the paths"    4 "$(has src/Rask.Ui/UiIconPaths.g.cs '"M1 2h4"u8')"
check "...and records nothing"                         "" "$(recorded)"

fresh
heroicons alpha 'M1 2h3'
publish heroicons 99.0.0
check "--check fails on another release's files"       stale "$(run flux/icons.mjs --check || echo stale)"
check "...and writes nothing"                          "" "$(unchanged "${icons[@]}")"

fresh
heroicons alpha 'M1 2h3'
publish heroicons 99.0.0 "sha512-$(printf 'not it' | openssl dgst -sha512 -binary | base64)"
check "a tarball the registry does not name is refused" refused "$(run flux/icons.mjs || echo refused)"
check "...and moves nothing"                           "" "$(unchanged "${icons[@]}")"

fresh
check "a registry that does not answer is not a failure" passed "$(run flux/icons.mjs && echo passed)"
check "...and moves nothing"                           "" "$(unchanged "${icons[@]}")"

echo "==> country-flag-icons"
# `flags [EXTRA]` puts 260 plain 3:2 flags (FR, IO and ES among them, never CP, DG or EA, which Unicode aliases
# to them) and a licence in the package; EXTRA is one more file's body, as ZZ.svg.
flags() {
  for a in A B C D E F G H I J K; do for b in A B C D E F G H I J K L M N O P Q R S T U V W X Y Z; do
    case "$a$b" in CP|DG|EA) continue ;; esac
    put "package/3x2/$a$b.svg" "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 513 342\"><path fill=\"#$a$b$a\" d=\"M0 0h513v342H0z\"/></svg>"
  done; done
  put package/3x2/IO.svg '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 513 342"><path d="M0 0h1"/></svg>'
  put package/LICENSE 'The MIT License (MIT) Copyright (c) 2099 nobody'
  [ -z "${1:-}" ] || put package/3x2/ZZ.svg "$1"
}
flag_files() { printf '%s\n' src/Rask.Ui/UiFlagRegistry.g.cs; (cd "$root" && ls src/Rask.Ui/Flags/*); }

fresh
flags
publish country-flag-icons 99.0.0
run flux/flags.mjs
check "a new release is written"                       1 "$(has src/Rask.Ui/UiFlagRegistry.g.cs 'internal const string Version = "99.0.0";')"
check "...naming its integrity"                        1 "$(has src/Rask.Ui/UiFlagRegistry.g.cs "$(sed -n 's/.*"integrity":"\([^"]*\)".*/\1/p' "$tmp/saved/$(fixture https://registry.npmjs.org/country-flag-icons/latest)")")"
check "...with its licence"                            1 "$(has src/Rask.Ui/Flags/LICENSE.txt 'country-flag-icons 99.0.0 — ')"
check "...its drawings"                                1 "$(has src/Rask.Ui/Flags/KZ.svg 'fill="#KZK"')"
check "...and none of the last release's left over"    0 "$(ls "$tmp/tree/src/Rask.Ui/Flags" | grep -c '^[L-Z].\.svg$' || true)"
check "--check passes on what it wrote"                passed "$(run flux/flags.mjs --check && echo passed)"

fresh
flags '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 3 2"><script>alert(1)</script></svg>'
publish country-flag-icons 99.0.0
check "a drawing that is not plain artwork is refused" refused "$(run flux/flags.mjs || echo refused)"
check "...and moves nothing"                           "" "$(unchanged $(flag_files))"

fresh
check "a registry that does not answer is not a failure" passed "$(run flux/flags.mjs && echo passed)"
check "...and moves nothing"                           "" "$(unchanged $(flag_files))"

echo "==> kit engines"
# A copy of ENGINE's package and bundles, with a stand-in npm (`install` locks what $tmp/lock.json says, `ci`
# installs nothing) and a build.mjs that writes each bundle at the size RASK_TEST_SIZES names.
engine() {
  fresh
  local dir="src/Rask.Ui/Resources/$1"
  mkdir -p "$tmp/tree/$dir" "$tmp/bin"
  cp "$root/scripts/upstream/kit-bundle.mjs" "$tmp/tree/scripts/upstream/"
  cp "$root/$dir/package.json" "$root/$dir/package-lock.json" "$tmp/tree/$dir/"
  cp "$root/src/Rask.Ui/Resources/ui-$1"*.js "$tmp/tree/src/Rask.Ui/Resources/"
  printf '#!/bin/sh\necho "npm $*" >> "%s/npm.log"\n[ "$1" != install ] || cp "%s/lock.json" package-lock.json\n' "$tmp" "$tmp" > "$tmp/bin/npm"
  chmod +x "$tmp/bin/npm"
  rm -f "$tmp/npm.log"
  printf '%s\n' "import { writeFileSync } from 'node:fs';" \
    "for (const [file, bytes] of Object.entries(JSON.parse(process.env.RASK_TEST_SIZES))) writeFileSync('../' + file, 'x'.repeat(bytes));" \
    > "$tmp/tree/$dir/build.mjs"
  engine_files=("$dir/package.json" "$dir/package-lock.json")
  for f in "$root/src/Rask.Ui/Resources/ui-$1"*.js; do engine_files+=("src/Rask.Ui/Resources/$(basename "$f")"); done
}
# `registry ENGINE [NAME=VERSION]... [--tamper]` answers npm's latest of every dependency: its pin, or VERSION.
# The lock npm would write goes to $tmp/lock.json (--tamper: with an integrity the registry does not name).
registry() {
  node "$tmp/registry.mjs" "$root/scripts/upstream/npm.mjs" "$tmp/tree/src/Rask.Ui/Resources/$1" "$tmp/saved" "$tmp/lock.json" "${@:2}"
}
cat > "$tmp/registry.mjs" <<'JS'
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
const [npm, dir, saved, out, ...specs] = process.argv.slice(2);
const { fixtureName } = await import(npm);
const manifest = JSON.parse(readFileSync(`${dir}/package.json`, 'utf8'));
const lock = JSON.parse(readFileSync(`${dir}/package-lock.json`, 'utf8'));
const asked = Object.fromEntries(specs.filter(s => s.includes('=')).map(s => s.split('=')));
for (const name of Object.keys({ ...manifest.dependencies, ...manifest.devDependencies })) {
  const entry = lock.packages[`node_modules/${name}`];
  const version = asked[name] ?? entry.version;
  const integrity = version === entry.version ? entry.integrity : `sha512-${createHash('sha512').update(`${name}@${version}`).digest('base64')}`;
  const answer = JSON.stringify({ name, version, dist: { tarball: `https://registry.npmjs.org/${name}/-/x-${version}.tgz`, integrity } });
  for (const tag of ['latest', version]) writeFileSync(`${saved}/${fixtureName(`https://registry.npmjs.org/${name.replace('/', '%2f')}/${tag}`)}`, answer);
  lock.packages[`node_modules/${name}`] = { ...entry, version, integrity: specs.includes('--tamper') ? 'sha512-AAAA' : integrity };
}
writeFileSync(out, JSON.stringify(lock, null, 2));
JS
sizes() { node -e 'const a = process.argv.slice(1), o = {}; for (let i = 0; i < a.length; i += 2) o[a[i]] = +a[i + 1]; console.log(JSON.stringify(o))' "$@"; }
bundle() { RASK_TEST_SIZES="$1" PATH="$tmp/bin:$PATH" run upstream/kit-bundle.mjs "${@:2}"; }
pin() { node -e 'const m = require(process.argv[1]); console.log({ ...m.dependencies, ...m.devDependencies }[process.argv[2]])' "$1/src/Rask.Ui/Resources/$2/package.json" "$3"; }
npm_ran() { cut -d' ' -f1-2 "$tmp/npm.log" 2>/dev/null | tr '\n' ' ' | sed 's/ $//'; }
tiptap="$(node -e 'console.log(Object.keys(require(process.argv[1]).dependencies).filter(n => n.startsWith("@tiptap/")).map(n => n + "=99.0.0").join(" "))' "$root/src/Rask.Ui/Resources/editor/package.json")"
editor_bytes="$(wc -c < "$root/src/Rask.Ui/Resources/ui-editor.js" | tr -d ' ')"

engine editor
registry editor
check "nothing new on npm still rebuilds from the lock"  passed "$(bundle '{}' editor && echo passed)"
check "...installs what the lock holds, locking nothing" "npm ci" "$(npm_ran)"
check "...and, the lock unmoved, moves nothing"         "" "$(unchanged "${engine_files[@]}")"

engine editor
registry editor @tiptap/core=99.0.0
check "a Tiptap family not yet at one version waits"  passed "$(bundle '{}' editor && echo passed)"
check "...and moves nothing"                           "" "$(unchanged "${engine_files[@]}")"

engine editor
# shellcheck disable=SC2086  # one NAME=VERSION a word
registry editor $tiptap
check "--check fails on a family behind npm's latest" stale "$(bundle '{}' editor --check || echo stale)"
check "a family at one new version lands when its bundle does not grow" passed \
  "$(bundle "$(sizes ui-editor.js "$editor_bytes")" editor && echo passed)"
check "...pinned"                                     99.0.0 "$(pin "$tmp/tree" editor @tiptap/starter-kit)"
check "...locked, installed and built"                 "npm install npm ci" "$(npm_ran)"
check "...esbuild kept at its own latest"              "$(pin "$root" editor esbuild)" "$(pin "$tmp/tree" editor esbuild)"
check "...and the prosemirror-model hold kept"         1.25.1 "$(node -e 'console.log(require(process.argv[1]).overrides["prosemirror-model"])' "$tmp/tree/src/Rask.Ui/Resources/editor/package.json")"

engine editor
# shellcheck disable=SC2086
registry editor $tiptap
check "a bundle that grows stops the run"              grown "$(bundle "$(sizes ui-editor.js $((editor_bytes + 1)))" editor || echo grown)"
check "...naming both sizes"                           1 "$(grep -c "ui-editor.js from $editor_bytes to $((editor_bytes + 1)) bytes" "$tmp/out" || true)"

engine editor
# shellcheck disable=SC2086
registry editor $tiptap --tamper
check "a lock that is not the registry's is refused"  refused "$(bundle "$(sizes ui-editor.js 1)" editor || echo refused)"
check "...before anything is installed"                "npm install" "$(npm_ran)"

engine phone
printf '<script src="/flux/phone.min.js?id=abc"></script>' > "$tmp/saved/$(fixture https://fluxui.dev/components/phone)"
printf '/* intl-tel-input */ var x={version:"29.9.9"};' > "$tmp/saved/$(fixture 'https://fluxui.dev/flux/phone.min.js?id=abc')"
registry phone intl-tel-input=29.9.9
printf '{"name":"intl-tel-input","version":"30.0.0","dist":{"tarball":"https://registry.npmjs.org/intl-tel-input/-/x-30.0.0.tgz","integrity":"sha512-BBBB"}}' \
  > "$tmp/saved/$(fixture https://registry.npmjs.org/intl-tel-input/latest)"
check "intl-tel-input follows the release Flux serves" passed "$(bundle "$(sizes ui-phone.js 1 ui-phone-utils.js 1)" phone && echo passed)"
check "...not npm's latest"                            29.9.9 "$(pin "$tmp/tree" phone intl-tel-input)"

engine phone
registry phone
check "a Flux page that does not answer moves nothing" passed "$(bundle '{}' phone && echo passed)"
check "...at all"                                      "" "$(unchanged "${engine_files[@]}")"

[ "$failures" -eq 0 ] || { echo "upstream node followers: $failures failed" >&2; exit 1; }
echo "upstream node followers: ok"
