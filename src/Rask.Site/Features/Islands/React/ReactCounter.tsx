// An ordinary React component. Nothing here imports Rask, and nothing here knows it is an island.
//
// The props type is GENERATED from ReactCounter.cs, so renaming a C# property stops this compiling —
// the contract is checked in both directions rather than maintained by discipline. It carries
// `children` too: whatever C# nests inside this island — text, or another React island such as the
// ColorPicker the demo puts here — arrives as ordinary React children.
//
// This file is also what "Preact rides the React adapter unchanged" means in practice: a Preact
// project aliases react/react-dom to preact/compat, and this source is byte-identical either way.
import { useEffect, useState } from 'react'
import type { ReactCounterProps } from '@rask/ReactCounter.props'

export default function ReactCounter({ step, caption, onTotalChanged, children }: ReactCounterProps) {
  // State React owns and C# never sees. Raising the step from C# must not reset it.
  const [total, setTotal] = useState(0)

  // The callback keeps its identity across updates, so this effect fires on a real change rather than
  // on every re-render — which is exactly what the runtime's handler cache buys.
  useEffect(() => {
    onTotalChanged?.(total)
  }, [total, onTotalChanged])

  return (
    <div className="react-counter" data-testid="react-counter">
      <div className="caption">{caption}</div>

      <button type="button" data-testid="react-add" onClick={() => setTotal(t => t + step)}>
        add {step}
      </button>

      <span className="total">
        total <strong data-testid="react-total">{total}</strong>
      </span>

      {children && (
        <div className="react-counter-children" data-testid="react-children">
          {children}
        </div>
      )}
    </div>
  )
}
