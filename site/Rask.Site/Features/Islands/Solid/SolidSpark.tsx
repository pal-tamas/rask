// An ordinary Solid component. Nothing here imports Rask, and nothing here knows it is an island.
//
// The props type is GENERATED from SolidSpark.cs, so renaming a C# property stops this compiling —
// the contract is checked in both directions rather than maintained by discipline.
//
// `props` is never destructured. Solid tracks the ACCESS rather than the value, so pulling `readings`
// out into a local here would read it once at mount and freeze it: the sparkline would render
// correctly and then never move again. That is the one Solid rule this file exists to demonstrate.
//
// Styled with TAILWIND, like the Vue chart beside it and for the same reason: an island is an ordinary
// part of the page and should reach the same design system the C# markup around it uses. A `.tsx` has
// no scoped-style block of its own, so the alternative here is not scoped CSS but NONE — which is how
// the first version of this file shipped bars with a percentage height inside a container that had no
// height, rendering them zero-sized. Playwright reported them as "element is not visible" and every
// interaction assertion timed out against a page that otherwise looked finished.
import { createSignal, For } from 'solid-js'
import type { SolidSparkProps } from '@rask/SolidSpark.props'

export default function SolidSpark(props: SolidSparkProps) {
  // State Solid owns and C# never sees. Raising the readings from C# must not reset it.
  const [hovers, setHovers] = createSignal(0)

  const peak = () => Math.max(1, ...props.readings)
  const height = (reading: number) => `${Math.max(4, Math.round((reading / peak()) * 100))}%`

  return (
    <figure class="m-0" data-testid="solid-spark">
      <figcaption class="mb-2 text-sm font-semibold tracking-wide text-slate-500 dark:text-slate-400">
        {props.caption}
      </figcaption>

      <div class="flex h-24 items-end gap-1">
        <For each={props.readings}>
          {(reading, index) => (
            <button
              type="button"
              class="bar flex-1 cursor-pointer rounded-t bg-emerald-600 transition-colors hover:bg-emerald-700"
              data-testid={`solid-bar-${index()}`}
              style={{ height: height(reading) }}
              onMouseEnter={() => {
                setHovers(h => h + 1)
                props.onPointHovered?.(index())
              }}
            >
              <span class="sr-only">{reading}</span>
            </button>
          )}
        </For>
      </div>

      <p class="mt-2 mb-0 text-sm text-slate-500 dark:text-slate-400">
        hovers <strong class="tabular-nums" data-testid="solid-hovers">{hovers()}</strong>
      </p>
    </figure>
  )
}
