#!/usr/bin/env bash
# gate-inputs: ^scripts/flux/Flux/
# The Flux harness's `refresh` command (scripts/flux/Flux/Commands/Refresh.cs), the catalogue half of the `flux` follower, run against saved docs pages: a part
# with a Reference heading of its own is read from it, and a part Flux only writes in its examples (or lists as
# an available item) is read from there — under `examples`, the props with the values written, never `options`.
# The daily run lands what this writes with nobody reading it, so it must write the same file every time.
#
# Usage:  scripts/tests/flux-refresh.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-flux-refresh.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT
failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}

mkdir -p "$tmp/saved"
printf '%s\n' '- [Menu](https://fluxui.dev/components/menu.md)' '- [Writer](https://fluxui.dev/components/writer.md)' \
  > "$tmp/saved/idx-llms.txt"
cat > "$tmp/saved/components-menu.md" <<'MD'
# Menu

```blade
<flux:menu>
    <flux:menu.group heading="Account" wire:key="a" class="mt-2">
        <flux:menu.item icon="user">Profile</flux:menu.item>
    </flux:menu.group>
    <flux:menu.group :heading="$title" x-on:click="open = true" />
    <flux:spacer />
    <flux:icon.bolt />
    <flux:chart.summary.value />
</flux:menu>
```

Prose that writes <flux:outside-code /> is no example.

## Reference

### flux:menu

**Prop:**

- `keep-open` - If `true`, stays open. Default: `false`.

### flux:menu.item

**Prop:**

- `variant` - The look. Options: `default`, `danger`.

### flux:icon.*

### flux:chart.summaryvalue
MD
cat > "$tmp/saved/components-writer.md" <<'MD'
# Writer

```blade
<flux:writer toolbar="bold | italic" />
<flux:menu.group heading="Billing" />
```

The following toolbar items are available:

- `bold`
- `italic`

Not an item:

- `nothing`

## Reference

### flux:writer
MD

# Built once into its own output and run twice: a `dotnet run` per call would rebuild, and nothing else in the
# gate writes this output.
dotnet build "$root/scripts/flux/Flux" -c Release -m:2 -nodeReuse:false -o "$tmp/flux" >"$tmp/build.log" 2>&1 \
  || { tail -20 "$tmp/build.log" >&2; echo "flux refresh: the harness did not build" >&2; exit 1; }
dotnet "$tmp/flux/flux.dll" refresh "$tmp/saved" "$tmp/first.json" >/dev/null
dotnet "$tmp/flux/flux.dll" refresh "$tmp/saved" "$tmp/second.json" >/dev/null
part() {
  jq -c --arg slug "$1" --arg name "$2" \
    'first(.pages[] | select(.slug == $slug) | .parts[] | select(.name == $name)) // null' "$tmp/first.json"
}

check "the same pages write the same file" same "$(cmp -s "$tmp/first.json" "$tmp/second.json" && echo same)"
check "a headed part is read from its Reference" \
  '{"name":"flux:menu.item","props":[{"name":"variant","options":["default","danger"]}]}' "$(part menu flux:menu.item)"
check "an unheaded part is read from its examples: props sorted, values literal, class and directives left out" \
  '{"name":"flux:menu.group","examples":[{"name":"heading","values":["Account"]}]}' "$(part menu flux:menu.group)"
check "...on every page that writes it" \
  '{"name":"flux:menu.group","examples":[{"name":"heading","values":["Billing"]}]}' "$(part writer flux:menu.group)"
check "a part written bare has no examples' props, and is still told from a headed one" '{"name":"flux:spacer","examples":[]}' "$(part menu flux:spacer)"
check "an icon is headed by flux:icon.*" null "$(part menu flux:icon.bolt)"
check "a part headed a dot fewer is headed" null "$(part menu flux:chart.summary.value)"
check "markup outside a code block is no example" null "$(part menu flux:outside-code)"
check "an available item is a part of its page's component" '{"name":"flux:writer.bold","examples":[]}' "$(part writer flux:writer.bold)"
check "...and only an item of that list" null "$(part writer flux:writer.nothing)"
check "the headed parts come first, in the Reference's order" \
  'flux:menu,flux:menu.item,flux:icon.*,flux:chart.summaryvalue,flux:menu.group,flux:spacer' \
  "$(jq -r '.pages[0].parts | map(.name) | join(",")' "$tmp/first.json")"

[ "$failures" -eq 0 ] || { echo "flux refresh: $failures failed" >&2; exit 1; }
echo "flux refresh: ok"
