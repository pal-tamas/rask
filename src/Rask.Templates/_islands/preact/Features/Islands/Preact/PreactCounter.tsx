// An ordinary Preact component. Nothing here imports Rask, and nothing here knows it is an island.
import { useEffect, useState } from 'preact/hooks'
import type { PreactCounterProps } from '@rask/PreactCounter.props'

export default function PreactCounter({ step, caption, onTotalChanged }: PreactCounterProps) {
  // State the front end owns and C# never sees. Raising the step from C# must not reset it.
  const [total, setTotal] = useState(0)

  useEffect(() => {
    onTotalChanged?.(total)
  }, [total, onTotalChanged])

  return (
    <div className="island-counter" data-testid="preact-counter">
      <div className="caption">{caption}</div>
      <button type="button" data-testid="preact-counter-add" onClick={() => setTotal(t => t + step)}>
        add {step}
      </button>
      <span className="total">
        total <strong data-testid="preact-counter-total">{total}</strong>
      </span>
    </div>
  )
}
