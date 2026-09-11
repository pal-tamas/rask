// An ordinary Solid component. Solid's signals are not React's hooks: the body runs once and the
// JSX re-reads the signal, which is why `total()` is a call.
import { createEffect, createSignal } from 'solid-js'
import type { SolidCounterProps } from '@rask/SolidCounter.props'

export default function SolidCounter(props: SolidCounterProps) {
  const [total, setTotal] = createSignal(0)

  createEffect(() => {
    props.onTotalChanged?.(total())
  })

  return (
    <div class="island-counter" data-testid="solid-counter">
      <div class="caption">{props.caption}</div>
      <button type="button" data-testid="solid-counter-add" onClick={() => setTotal(t => t + props.step)}>
        add {props.step}
      </button>
      <span class="total">
        total <strong data-testid="solid-counter-total">{total()}</strong>
      </span>
    </div>
  )
}
