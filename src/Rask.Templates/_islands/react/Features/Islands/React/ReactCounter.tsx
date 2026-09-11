// An ordinary React component. Nothing here imports Rask, and nothing here knows it is an island.
import { useEffect, useState } from 'react'
import type { ReactCounterProps } from '@rask/ReactCounter.props'

export default function ReactCounter({ step, caption, onTotalChanged }: ReactCounterProps) {
  // State the front end owns and C# never sees. Raising the step from C# must not reset it.
  const [total, setTotal] = useState(0)

  useEffect(() => {
    onTotalChanged?.(total)
  }, [total, onTotalChanged])

  return (
    <div className="island-counter" data-testid="react-counter">
      <div className="caption">{caption}</div>
      <button type="button" data-testid="react-counter-add" onClick={() => setTotal(t => t + step)}>
        add {step}
      </button>
      <span className="total">
        total <strong data-testid="react-counter-total">{total}</strong>
      </span>
    </div>
  )
}
