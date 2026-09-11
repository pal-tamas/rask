#!/usr/bin/env bash
# Re-import a front-end template from its framework's own creator.
#
# The template trees under src/Rask.Templates/ are committed, which is what makes a scaffold
# deterministic, offline, reviewable and visible to Dependabot. The cost of that is stated plainly:
# a tree is a SNAPSHOT of what its creator wrote on the day it was imported, and this script is how a
# newer one gets in. Upstream drift arrives as a reviewed commit instead of changing silently under
# every user, which is what `npx create-vite@latest` at scaffold time used to mean.
#
# It does NOT overwrite the tree. It writes the creator's fresh output to a scratch directory and
# prints the diff, because a template also carries Rask's own files and the `rask:if` markers that
# express the battery conditionals — neither of which the creator knows about, and both of which a
# blind copy would destroy. Read the diff, take the parts that matter, re-run the lockfile and lint
# passes, and commit.
#
# Usage:
#   scripts/refresh-templates.sh              # every front-end template
#   scripts/refresh-templates.sh react nuxt   # only these
#
# After applying anything:
#   (cd src/Rask.Templates/<t>/client && npm install --package-lock-only && npx eslint . )
#   dotnet test tests/Rask.Cli.Tests   # the pin, integrity and contract suites
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
templates="$root/src/Rask.Templates"
scratch="${RASK_REFRESH_DIR:-$(mktemp -d)}"

# The creator for each template, and the flags that are load-bearing rather than taste.
#
# Every invocation is NON-INTERACTIVE: these creators all prompt by default, and a prompt inside an
# unattended refresh is a hang rather than a failure anyone can act on.
#
# Two facts the creators cannot know, and that Rask has to supply afterwards, are the reason this is a
# re-import rather than a fresh scaffold: the build must emit a NODE SERVER (a static or edge preset
# produces an app whose entry never exists), and the dev server must proxy /_rask back to the host.
# Both live in the committed tree's own config files — which is exactly why the diff is read, not applied.
creator_for() {
  case "$1" in
    react)          echo "create-vite@latest client --template react-ts" ;;
    preact)         echo "create-vite@latest client --template preact-ts" ;;
    vue)            echo "create-vite@latest client --template vue-ts" ;;
    solid)          echo "create-vite@latest client --template solid-ts" ;;
    svelte)         echo "create-vite@latest client --template svelte-ts" ;;
    lit)            echo "create-vite@latest client --template lit-ts" ;;
    angular)        echo "@angular/cli@latest new client --directory client --style css --ssr false --skip-git --skip-install" ;;
    nuxt)           echo "nuxi@latest init client --template minimal --packageManager npm --no-gitInit --no-install" ;;
    nextjs)         echo "create-next-app@latest client --ts --app --no-src-dir --no-eslint --tailwind --use-npm --skip-install --disable-git --yes" ;;
    sveltekit)      echo "sv@latest create client --template minimal --types ts --add sveltekit-adapter=adapter:node tailwindcss=plugins:typography --no-install --no-dir-check --no-download-check" ;;
    tanstack-start) echo "@tanstack/cli create client --framework react --deployment nitro --non-interactive --no-install --no-git" ;;
    solidstart)     echo "create-solid@latest client --solidstart --v2 --ts -t with-tailwindcss" ;;
    analog)         echo "create-analog@latest client --template angular-v20 --skipTailwind" ;;
    *)              return 1 ;;
  esac
}

all="react preact vue solid svelte lit angular nuxt nextjs sveltekit tanstack-start solidstart analog"
wanted="${*:-$all}"

echo "Scratch: $scratch"
status=0

for template in $wanted; do
  if ! args="$(creator_for "$template")"; then
    echo "!! unknown template '$template' (known: $all)" >&2
    status=1
    continue
  fi

  if [ ! -d "$templates/$template/client" ]; then
    echo "!! $template has no client/ to compare against" >&2
    status=1
    continue
  fi

  echo
  echo "=== $template"
  echo "    npx --yes $args"
  work="$scratch/$template"
  rm -rf "$work"; mkdir -p "$work"

  # shellcheck disable=SC2086
  if ! (cd "$work" && npx --yes $args >"$work/.creator.log" 2>&1); then
    echo "    creator FAILED:"
    tail -8 "$work/.creator.log" | sed 's/^/      /'
    status=1
    continue
  fi

  # A .gitignore is stored without its dot in the template tree (a nested one is read as a rule for
  # THIS repository and drops payload files), so the creator's is renamed before comparing or the
  # diff reports it as added and removed at once.
  if [ -f "$work/client/.gitignore" ]; then
    mv "$work/client/.gitignore" "$work/client/gitignore"
  fi

  # Rask's own files and the marker regions are not the creator's to comment on; the diff is limited
  # to files the creator actually wrote.
  diff -ru --new-file \
    --exclude=node_modules --exclude=.git --exclude=.creator.log \
    --exclude=package-lock.json --exclude=eslint.config.mjs \
    --exclude=.prettierrc --exclude=.prettierignore \
    "$templates/$template/client" "$work/client" \
    | sed 's/^/    /' || true
done

echo
echo "Nothing was changed. Apply what matters by hand, then re-run the lockfile and lint passes."
exit "$status"
