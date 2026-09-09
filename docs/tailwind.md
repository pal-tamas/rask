# Tailwind CSS

Rask compiles [Tailwind CSS](https://tailwindcss.com) **from the SDK**, on every host, with nothing
installed. No `package.json`, no `node_modules`, no PostCSS step, no npm — `dotnet build` produces the
stylesheet, and `dotnet build` is the only thing anyone needs in order to build your app.

```bash
rask new Shop
cd Shop
rask dev
```

That is the whole setup — and there was no step you skipped. Styling is
[not a choice `rask new` offers](cli.md#rask-new--scaffold-a-project): every project is a Tailwind
project, with no flag to pass, nothing to turn on, and nothing to turn off.

**And a daisyUI project.** [daisyUI](ui-kit.md) is a Tailwind plugin — component classes like `btn`,
`card` and `navbar` on top of the utilities — and it arrives the same way: already there, no npm,
nothing to install. It is what the scaffolded starter page is written in, on every template `rask new`
can emit, so a project looks the same whether its front end is C#, React or Nuxt.

It works on every template. On `wasm` the stylesheet belongs to the **browser**
project — Tailwind scans the tree it runs in, and the components whose classes it is looking for are
the client's. The compiler is a build-time tool with no runtime assembly, so it adds nothing to what
the browser downloads.

## What a new project starts with

One file and two links — the whole of it, and all of it already there:

1. **`Styles/app.css`** — the stylesheet Tailwind compiles:

   ```css
   @layer properties, theme, base, components, daisyui, utilities;

   @import "tailwindcss";

   @source not "./vendor";
   @plugin "./vendor/daisyui.mjs";

   /* Your own CSS goes here. Anything below participates in the same build, so @apply and
      @theme work, and the output still contains only what this project actually uses. */
   ```

   Still no config file, no `content` array and no `tailwind.config.js` — v4 detects its own sources.
   The three lines around the import are [daisyUI](ui-kit.md), and each one is load-bearing:

   - **`@plugin`** loads daisyUI from a copy `Rask.Ui` ships and the build puts beside this file. By
     relative path, because Tailwind resolves a plugin the way Node does — by walking up for a
     `node_modules` — and the standalone engine below carries no package tree. **There is still no npm
     and no `node_modules`.**
   - **`@source not`** says the bundle is not source. Tailwind scans the directory it runs in, and that
     file names every class daisyUI defines; scanned, it acts as a safelist for the whole library, and a
     sheet containing too much looks exactly like a sheet containing enough.
   - **`@layer`** declares the order before anything can imply another. daisyUI emits into a `daisyui`
     layer that Tailwind's own import does not rank, so left alone it outranks the utilities beside it
     and `class="btn px-8"` gives you `.btn`'s padding, not `px-8`.

2. **Two `<link>`s in the app shell**, in an order that is a contract:

   ```csharp
   // The kit's sheet, FIRST — it declares the @layer order for the whole document.
   Link.Rel("stylesheet").Href(UiStylesheet.Href(LiveOptions.PathBase)),
   // Compiled from Styles/app.css by Rask.Tailwind, scanning this project's own source.
   Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
   ```

   A browser ranks `@layer` names by first appearance across every sheet on the page, and nothing later
   can reorder a name already placed — so linking the kit's second lets the ranking fall out of
   whichever sheet happened to mention a name earliest. That put `base` above `utilities` for a whole
   document once, and every `text-4xl` and `px-*` in the markup was silently beaten by preflight.

**Nothing else in the `.csproj` but two opt-ins.** There is still no Tailwind package to add — the
compiler, its MSBuild targets and the task that fetches it ship *inside* `Rask.Server` and `Rask.Wasm`,
the way scoped CSS does. What a scaffolded project adds is `<RaskUiWriteStylesheet>` and
`<RaskUiWriteDaisyUiPlugin>`, which are what write those two files into the tree. Both are build-only:
no runtime assembly, nothing shipped with the app.

## Your C# is the source it scans

Tailwind v4 finds class names by scanning the project directory, and a C# component's classes are
ordinary string literals — so they are found with nothing telling it to:

```csharp
Div.Class("rounded-lg border border-slate-200 p-6 shadow-sm")[
    H1.Class("text-2xl font-semibold tracking-tight")["Shop"]
]
```

Add a utility to a `Render()` body, rebuild, and it is in the stylesheet. Remove it and it is gone —
the output holds only what the project actually uses, so it stays small without you curating it.

This is why the build runs Tailwind **with the project as its working directory**: v4 resolves its
sources relative to where it runs, and running it anywhere else scans the wrong tree and emits an
almost-empty stylesheet with no error at all.

It is also why the input sheet is taken out of `AdditionalFiles` before compilation. Rask's
[scoped CSS](js-interop.md) claims `**/*.css`, and without the exclusion your Tailwind input would be
treated as a component's scoped stylesheet — [RASK015](diagnostics.md), for a file that is not one.

## Where the engine comes from

Tailwind v4's compiler is a native binary, and Rask fetches it rather than asking you to:

| | |
|---|---|
| **Standalone binary** (preferred) | Downloaded once from Tailwind's GitHub releases, verified against the release's published checksum, and cached **per user** at `~/.rask/tailwind` (`%LOCALAPPDATA%\rask\tailwind` on Windows) — shared by every project, deliberately outside the repository. |
| **npm** (fallback) | A project-local `npm install` of `tailwindcss`, used where no standalone binary is published. |

The standalone binary is first because "the SDK is all you need" is most of why anyone picks a C#
host, and it keeps that true on macOS (x64/arm64), Linux (x64/arm64, glibc **and** musl) and Windows
x64. npm is second because it covers strictly more: Tailwind's npm engine ships native builds for
win32-arm64, 32-bit ARM and FreeBSD that the standalone release has no equivalent of, plus a
`wasm32-wasi` build that runs anywhere Node does. The standalone release publishes seven assets;
between the two engines **no platform is simply unsupported**.

The fallback is a real `npm install` into the project, not `npx`: `npx --package tailwindcss` and
`npx --cwd` both fail to place the platform-specific optional dependency the engine needs.

## Knobs

Every one of these is an MSBuild property: set it in the `.csproj`, or pass `-p:Name=value`. They
change *how* the stylesheet is built, never *whether* — **there is no off switch**, and that is
deliberate. Every page of a Rask app is written in utilities, so a build that quietly produced no CSS
would serve unstyled HTML: a failure nobody notices until a user does, and one no test of your C# can
see. What decides whether the compiler runs is simply whether the project has a `Styles/app.css` to
compile, which is what lets a class library in the same solution ignore all of this.

| Property | Default | What it does |
|---|---|---|
| `RaskTailwindVersion` | `4.3.3` | The Tailwind version. **Pinned, never floating** — a compiler is not a library, and a different version emits different CSS, so a build that quietly picked up a new one would change how your pages look with nothing in the diff. Bump it deliberately. |
| `RaskTailwindEngine` | `auto` | `standalone` or `npm` to force one. On Windows on ARM, `npm` gets you a native engine instead of the x64 binary under emulation. |
| `RaskTailwindInput` | `Styles/app.css` | Your stylesheet — the one with `@import "tailwindcss"`. |
| `RaskTailwindOutput` | `wwwroot/css/app.css` | Where the compiled CSS lands. |
| `RaskTailwindMinify` | `true` in Release | Minified for production, readable in devtools while you work. |
| `RaskTailwindOffline` | `false` | Never reach the network. A missing binary fails the build naming the file to place and the exact path it goes at, instead of downloading it. For builds that must be hermetic. |
| `RaskTailwindCacheRoot` | `~/.rask/tailwind` | Where fetched binaries are cached. |

The build is **incremental**: it re-runs only when the input sheet or a `.cs`/`.razor`/`.html` file in
the project has changed since the output was written. Design-time builds are excluded entirely — an
IDE reloading a project must never download a binary or shell out to a compiler.

## When something goes wrong

Every failure names the way out, and the way out is always available — it is a way *through*, never a
way to skip the stylesheet:

- **No standalone binary for this platform, and no Node.js.** The error names your OS and
  architecture and gives the install line for it (`brew install node`, `winget install
  OpenJS.NodeJS.LTS`, your distro's `nodejs` package). Between the two engines no platform is
  unsupported, so installing Node is always an available answer — and it is the answer, because there
  is no build of a Rask app without its stylesheet.
- **`RaskTailwindEngine=standalone` on a platform with no binary.** Refused rather than silently
  falling back, because you asked for the binary specifically.
- **Offline, with nothing cached.** The error prints the download URL and the exact path to put it
  at. The cache is per user rather than per project, so seeding it once serves every build on the
  machine — which is how a hermetic or air-gapped build is set up.
- **The download failed.** Not fatal on its own — a machine that cannot reach GitHub releases can often
  still reach a registry mirror, so the build says so and tries npm.

## Front-end templates

The [TypeScript templates](spa.md) get Tailwind too, and there it works the way that ecosystem
expects: `@tailwindcss/vite` in the client's own `package.json` and Vite config, not the standalone
binary. That project already has Node, a bundler and a dev server with HMR — routing its CSS through
MSBuild instead would be strictly worse. The C# side of the solution is untouched.

```bash
rask new Shop --template react
```

The generated global stylesheet **replaces** create-vite's starter CSS rather than sitting beside it,
because leaving it in would fight Tailwind's own reset. It carries a `@layer base` as well as the
import: part of the file being replaced styles the placeholder page the template overlaid away, but
the rest styles `body`, `h1` and `p` **by tag**, and those tags are what the starter still renders.
Import alone and preflight leaves the page as unstyled text. See
[Styling](spa.md#styling) for what that layer contains and how to move it into your own markup.

## See also

- [The `rask` CLI](cli.md) — `rask new`, and which template supports which flag.
- [TypeScript front ends](spa.md) — the SPA templates and their build.
- [Scoped CSS and JS](js-interop.md) — the per-component styling Tailwind sits beside, not instead of.
