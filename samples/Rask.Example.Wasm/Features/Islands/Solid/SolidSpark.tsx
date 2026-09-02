// An ordinary Solid component. Nothing here imports Rask, and nothing here knows it is an island.
//
// The props type is GENERATED from SolidSpark.cs, so renaming a C# property stops this compiling —
// the contract is checked in both directions rather than maintained by discipline.
//
// `props` is never destructured. Solid tracks the ACCESS rather than the value, so pulling `readings`
// out into a local here would read it once at mount and freeze it: the sparkline would render
// correctly and then never move again. That is the one Solid rule this file exists to demonstrate.
import { createSignal, For } from 'solid-js'
import type { SolidSparkProps } from '@rask/SolidSpark.props'

export default function SolidSpark(props: SolidSparkProps) {
  // State Solid owns and C# never sees. Raising the readings from C# must not reset it.
  const [hovers, setHovers] = createSignal(0)

  const peak = () => Math.max(1, ...props.readings)

  return (
    <div class="solid-spark" data-testid="solid-spark">
      <div class="caption">{props.caption}</div>

      <div class="bars">
        <For each={props.readings}>
          {(reading, index) => (
            <button
              type="button"
              class="bar"
              data-testid={`solid-bar-${index()}`}
              style={{ height: `${Math.round((reading / peak()) * 100)}%` }}
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

      <span class="hovers">
        hovers <strong data-testid="solid-hovers">{hovers()}</strong>
      </span>
    </div>
  )
}
