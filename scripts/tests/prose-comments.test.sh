#!/usr/bin/env bash
# gate-inputs: ^src/.*\.cs$
# A `//` comment line in src/**/*.cs must not end in ';' (Sonar S125, "Remove this commented out code").
#
# Sonar decides whether such a line is prose by matching it against a sentence regex with a 100 ms
# WALL-CLOCK timeout, and swallows the timeout: on a starved runner the match gives up and a sentence
# that ends in ';' becomes "code". The same file built green in eight jobs of one run and red in the
# ninth (#1216). The rule cannot be made deterministic from here, so the shape that trips it is kept
# out of the tree: end the line with a full stop, a comma or a dash instead.
#
# Doc comments (`///`) are not S125's subject and are left alone, and so are generated files
# (`*.g.cs`, `*.Designer.cs`, anything under obj/ or bin/), which nobody rewords by hand.
#
# Usage:  scripts/tests/prose-comments.test.sh   (run by scripts/run-unit-local.sh)
#         scripts/tests/prose-comments.test.sh FILE...   checks just those files
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

if [ "$#" -gt 0 ]; then
  files=("$@")
else
  files=()
  while IFS= read -r -d '' f; do files+=("$f"); done < <(git ls-files -z -- 'src/*.cs')
fi

# A comment line ending in ';' — `//` then a character that is not a third '/', so `///` is excluded.
pattern='^[[:space:]]*//[^/].*;[[:space:]]*$'

# The pattern itself, first: a guard that silently matches nothing is no guard.
probe() { # expected(0 match / 1 no match) line
  if printf '%s\n' "$2" | grep -E "$pattern" >/dev/null; then got=0; else got=1; fi
  if [ "$got" != "$1" ]; then
    echo "prose-comments: the pattern misjudges: $2" >&2
    exit 1
  fi
}
probe 0 '    // the log is held here;'
probe 0 '// trailing space after it; '
probe 1 '    /// <summary>A doc line;</summary>;'
probe 1 '    // ends with a full stop.'
probe 1 '    var x = 1; // a trailing comment'

found=0
for f in "${files[@]}"; do
  case "$f" in
    *.g.cs | *.Designer.cs | */obj/* | */bin/*) continue ;;
  esac
  [ -f "$f" ] || continue
  # `|| true`: grep exits 1 on no match, which is the passing case.
  hits="$(grep -nE "$pattern" "$f" || true)"
  [ -z "$hits" ] && continue
  while IFS= read -r hit; do
    printf '%s:%s: a // comment line ends in ";" (Sonar S125 flags it as commented-out code under load, #1216). End it with a full stop, comma or dash.\n' \
      "$f" "${hit%%:*}" >&2
    found=$((found + 1))
  done <<<"$hits"
done

if [ "$found" -gt 0 ]; then
  echo "prose-comments: $found comment line(s) end in ';' — see above." >&2
  exit 1
fi
echo "  ok    no // comment line in src/**/*.cs ends in ';' (${#files[@]} files)"
