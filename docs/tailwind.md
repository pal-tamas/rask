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
`card` and `navbar` on top of the utilities — and it arrives with [the UI kit](ui-kit.md), in the same
import: already there, no npm, nothing to install. It is what the scaffolded starter page is written in, on every template `rask new`
can emit, so a project looks the same whether it runs on the server or in WebAssembly.

It works on every template. On `wasm` the stylesheet belongs to the **browser**
project — Tailwind scans the tree it runs in, and the components whose classes it is looking for are
the client's. The compiler is a build-time tool with no runtime assembly, so it adds nothing to what
the browser downloads.

## What a new project starts with

One file and one link — the whole of it, and all of it already there:

1. **`Styles/app.css`** — the stylesheet Tailwind compiles:

   ```css
   @import "./vendor/rask-ui.css";

   /* Your own CSS goes here. Anything below participates in the same build, so @apply and
      @theme work, and the output still contains only what this project actually uses. */
   ```

   Still no config file, no `content` array and no `tailwind.config.js` — v4 detects its own sources.
   That one import is Tailwind **and** [the UI kit](ui-kit.md), so the app compiles **one stylesheet**,
   the way a [Flux](https://fluxui.dev) app does. `rask-ui.css` is four lines:

   ```css
   @layer properties, theme, base, components, daisyui, rask, utilities;

   @import "tailwindcss";
   @import "./rask-ui.kit.css";
   @source "./rask-ui.classes.txt";
   ```

   - **`@layer`** declares the order for the document before anything can imply another: your
     utilities above preflight, above daisyUI and above the kit's own rules. It is why the import goes
     first in your sheet.
   - **`rask-ui.kit.css`** is the kit as Tailwind source: its `@theme` tokens, the `dark` variant its
     components and your `dark:` utilities share, [daisyUI](ui-kit.md) (loaded from `daisyui.mjs`
     beside it, by relative path — the standalone engine carries no package tree, so there is still
     **no npm and no `node_modules`**), and the few rules a utility cannot say.
   - **`rask-ui.classes.txt`** is every class the kit's components write. They are C# string literals
     in a compiled assembly, where no scan can find them, so the kit's build lists them and yours reads
     the list.

   The build writes those files into `Styles/vendor/` before Tailwind runs; `rask new` ignores the
   folder. You do not edit or commit them.

2. **One `<link>`.** `RaskApp` and the WASM host write it into every document — the build records
   where `css/app.css` is served as the app assembly's `Rask.Stylesheet` metadata — so `App.cs` names
   no stylesheet. A hand-wired `MapRask<App>()` host writes it itself:

   ```csharp
   // Compiled from Styles/app.css by Rask.Tailwind: Tailwind, the kit, and this project's own classes.
   Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
   ```

**Nothing in the `.csproj`.** There is no Tailwind package to add — the compiler, its MSBuild targets
and the task that fetches it ship *inside* `Rask.Server` and `Rask.Wasm`, the way scoped CSS does — and
no switch for the kit: the import in `Styles/app.css` is the whole opt-in. The build reads it, writes
`Styles/vendor/`, and tells the host the kit is already in the app's sheet.

### One sheet, not two

Until this arrangement an app linked two: the kit's precompiled sheet, then its own. Both put utilities
in `@layer utilities`, and between two sheets the cascade has nothing left to rank them by but link
order — so any base utility *you* wrote anywhere beat a kit **variant** of the same property. On
rask.sh a Flux card (`bg-white dark:bg-white/4`) computed `rgb(255, 255, 255)` in dark mode because the
site writes `bg-white` on some other element, and a kit `sm:flex-row` lost to the site's `flex-col`.
Every class in the markup was right.

In one sheet every utility exists once and Tailwind's own order holds: a base utility before its
variants, a shorthand before its longhands. So the build **refuses** the old pairing —
`<RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>` in a project that compiles its own Tailwind
stylesheet is an error, with the line to write instead.

**Upgrading an app scaffolded before this:**

```diff
  /* Styles/app.css */
- @layer properties, theme, base, components, daisyui, utilities;
-
- @import "tailwindcss";
-
- @source not "./vendor";
- @plugin "./vendor/daisyui.mjs";
+ @import "./vendor/rask-ui.css";
```

```diff
  <!-- the .csproj -->
- <RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>
- <RaskUiWriteDaisyUiPlugin>true</RaskUiWriteDaisyUiPlugin>
```

and, in a hand-wired host, drop the `UiStylesheet.Href()` link. `wwwroot/css/rask-ui.css` is deleted by
the next build.

### Without Tailwind's preflight, or with a layer of your own

`rask-ui.css` is short so that you can write it out. A sheet that wants Tailwind's utilities without
its reset imports the kit's own file and says the rest itself — this is what `Rask.Dashboard` does:

```css
@layer properties, theme, base, components, daisyui, rask, utilities;

@import "tailwindcss/theme.css" layer(theme);
@import "tailwindcss/utilities.css" layer(utilities);
@import "./vendor/rask-ui.kit.css";
@source "./vendor/rask-ui.classes.txt";
```

To add a layer, name it in a statement of your own **before** the import: `@layer theme, base, brand;`
puts `brand` above `base` and below everything the kit's statement adds after it.

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
| **Standalone binary** (preferred) | Downloaded once from Tailwind's GitHub releases, verified against a SHA-256 **recorded in Rask itself** for the pinned version — a download that does not match fails the build, it never falls back to npm — and cached **per user** at `~/.rask/tailwind` (`%LOCALAPPDATA%\rask\tailwind` on Windows) — shared by every project, deliberately outside the repository. |
| **npm** (fallback) | A project-local `npm install` of `tailwindcss`, used where no standalone binary is published. |

The release's own `sha256sums.txt` comes from the same place as the binary, so it can only show that a
download arrived intact. That is still what an app that overrides `RaskTailwindVersion` gets, with a
build warning saying so; set `RaskTailwindSha256` to the asset's SHA-256 to pin that version too.

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
| `RaskTailwindInput` | `Styles/app.css` | Your stylesheet — the one that imports Tailwind, on its own or through `rask-ui.css`. |
| `RaskTailwindOutput` | `wwwroot/css/app.css` | Where the compiled CSS lands. |
| `RaskTailwindMinify` | `true` in Release | Minified for production, readable in devtools while you work. |
| `RaskTailwindOffline` | `false` | Never reach the network. A missing binary fails the build naming the file to place and the exact path it goes at, instead of downloading it. For builds that must be hermetic. |
| `RaskTailwindCacheRoot` | `~/.rask/tailwind` | Where fetched binaries are cached. |
| `RaskTailwindClassList` | unset | For a component **library** that compiles a sheet of its own: a path to write every class that sheet defines, one per line, for an app's Tailwind build to read with `@source`. It is how `Rask.Ui` hands its classes over. |
| `RaskUiTailwind` | follows the stylesheet | Whether the build writes the kit's Tailwind sources into `Styles/vendor/`: on when the stylesheet imports `rask-ui.css` (or `rask-ui.kit.css`), off otherwise. `true`/`false` decides it regardless. |
| `RaskUiTailwindDirectory` | `vendor/` beside the stylesheet | Where those sources land. |

The build is **incremental**: it re-runs only when the input sheet, anything beside it (a sheet it
imports, the kit's files in `Styles/vendor/`), or a `.cs`/`.razor`/`.html` file in the project has
changed since the output was written. Design-time builds are excluded entirely — an
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

## See also

- [The `rask` CLI](cli.md) — `rask new`, and which template supports which flag.
- [Scoped CSS and JS](js-interop.md) — the per-component styling Tailwind sits beside, not instead of.
