#!/usr/bin/env bash
# gate-inputs: .
# Rask needs no Node: not to build, test or run an app, not to maintain the repo, not in CI. The compilers a build
# runs (tsgo, esbuild, Tailwind) are native binaries Rask downloads by digest, the followers are Python or .NET, and
# the browser is driven by Microsoft.Playwright (.NET). scripts/lib/no_node.py holds every tracked file there:
# no npm or Node file in the tree, and nothing that RUNS node/npm/npx/pnpm/yarn/bun, sets Node up or installs it.
#
# It first proves itself on saved samples — what it must flag and what it must leave alone (a JSON key "node" in
# C#, "npm's latest" in a message, npm's registry URL, a comment) — then reads the tree. A Node a dependency
# carries inside itself (Microsoft.Playwright's driver) is that package's business, never invoked from here.
#
# Usage:  scripts/tests/no-node.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
scanner="$root/scripts/lib/no_node.py"
tmp="$(mktemp -d -t rask-no-node.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}
# `sample PATH TEXT` writes one file; `flagged PATH` is how many uses the scanner finds in it.
sample() { mkdir -p "$(dirname "$tmp/$1")"; printf '%s\n' "$2" > "$tmp/$1"; }
flagged() { (cd "$tmp" && printf '%s\0' "$1" | python3 -I "$scanner" 2>&1 >/dev/null | grep -c '^  FAIL' || true); }

echo "==> no node: the scanner"
sample a/run.sh 'npm ci'
check "a script that runs npm"                           1 "$(flagged a/run.sh)"
sample a/chain.sh 'cd client && npx vite build'
check "...after an operator"                             1 "$(flagged a/chain.sh)"
sample a/sub.sh 'echo "found $(node --version)"'
check "...inside a substitution, quoted or not"          1 "$(flagged a/sub.sh)"
sample a/sudo.sh 'sudo env CI=1 pnpm install'
check "...behind sudo and env"                           1 "$(flagged a/sudo.sh)"
sample a/apt.sh 'apt-get install -y nodejs'
check "a script that installs Node"                      1 "$(flagged a/apt.sh)"
sample a/bun.sh 'if x; then bun run build; fi'
check "...or runs bun"                                   1 "$(flagged a/bun.sh)"
sample .github/workflows/w.yml $'jobs:\n  a:\n    steps:\n      - uses: actions/setup-node@v7\n      - run: node scripts/x.js\n      - run: |\n          yarn install'
check "a workflow that sets Node up and runs it"         3 "$(flagged .github/workflows/w.yml)"
sample a/Dockerfile $'FROM x\nRUN npm ci && \\\n    npm run build'
check "a Dockerfile's RUN, continued"                    2 "$(flagged a/Dockerfile)"
sample a/b.targets $'<Project>\n  <Target Name="T">\n    <Exec Command="npm ci" />\n    <Exec WorkingDirectory="x"\n          Command="dotnet build &amp;&amp; node build.js" />\n  </Target>\n</Project>'
check "an MSBuild Exec, on one line or several"          2 "$(flagged a/b.targets)"
sample a/i.ps1 $'& npm install\n$v = (& node --version)'
check "a PowerShell call"                                2 "$(flagged a/i.ps1)"
sample a/P.cs $'var p = new ProcessStartInfo("npm", "ci");\nvar q = new ProcessStartInfo { FileName = "node" };\nvar r = OperatingSystem.IsWindows() ? "npm.cmd" : "x";'
check "a .NET process started on npm or node"            3 "$(flagged a/P.cs)"
sample a/p.py $'subprocess.run(["node", "x.js"])\nos.system("npm ci")'
check "a Python process started on node or npm"          2 "$(flagged a/p.py)"
sample a/install $'#!/usr/bin/env bash\ncurl -fsSL https://nodejs.org/dist/v24.0.0/node.tar.xz'
check "a shebang script that downloads Node"             1 "$(flagged a/install)"
for file in package.json web/package-lock.json .npmrc .nvmrc .node-version tools/x.mjs tools/y.cjs yarn.lock; do
  sample "$file" '{}'
  check "a tracked $file"                                1 "$(flagged "$file")"
done

sample b/Keys.cs $'states.Add(new JsonObject { ["node"] = target });\nvar n = s["node"];\nthrow Fail($"npm\'s latest {name} is {v}");\n// node scripts/x.js was the old way\nvar url = "https://registry.npmjs.org/heroicons/latest";\nvar node = tree.FirstChild;'
check "C# naming a node, npm's registry or Node in prose" 0 "$(flagged b/Keys.cs)"
sample b/py.py $'"""npm\'s registry, read over HTTPS."""\nraise ShapeChanged(f"npm\'s {v} {name}")\nURL = "https://registry.npmjs.org/"\nnode = dom.firstChild'
check "Python naming npm in a message or a URL"           0 "$(flagged b/py.py)"
sample b/s.sh $'# node scripts/flux/x.mjs is gone\necho \'npm ci is not run any more\'\nprintf "%s\\n" "sudo apk add nodejs (an old hint)"\nnode_modules=0; ls nodes\ncurl https://registry.npmjs.org/x/latest\ndotnet build -nodeReuse:false'
check "a script that only says npm, quotes it or names a node" 0 "$(flagged b/s.sh)"
sample b/w.yml $'      - uses: actions/setup-dotnet@v6\n        with:\n          node-version-file: x\n      # - uses: actions/setup-node was here\n      - name: node is gone'
check "a workflow whose keys or names say node"          0 "$(flagged b/w.yml)"
sample b/c.targets $'<!-- <Exec Command="npm ci" /> -->\n<Exec Command="dotnet run" />\n<NodeReuse>false</NodeReuse>'
check "MSBuild with Node only in a comment or a name"    0 "$(flagged b/c.targets)"
sample b/README.md 'Run `npm ci` in the old days.'
check "prose in Markdown"                                0 "$(flagged b/README.md)"

[ "$failures" -eq 0 ] || { echo "no node: the scanner failed $failures sample(s)" >&2; exit 1; }

echo "==> no node: the tree"
cd "$root"
git ls-files -z | python3 -I "$scanner" scripts/tests/no-node.test.sh scripts/lib/no_node.py
