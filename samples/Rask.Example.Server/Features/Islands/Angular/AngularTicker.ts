// An ordinary standalone Angular component. Nothing here imports Rask, and nothing here knows it is
// an island.
//
// The props type is GENERATED from AngularTicker.cs, so renaming a C# property stops this compiling —
// the contract is checked in both directions rather than maintained by discipline.
//
// Every prop is an @Input(). That is not decoration: the adapter drives updates through
// ComponentRef.setInput, which is the only route that marks the view dirty, and a plain public field
// is not an input at all — Angular says so in a development build and ignores it silently in a
// production one.
import { Component, Input } from '@angular/core'
import type { AngularTickerProps } from '@rask/AngularTicker.props'

@Component({
  selector: 'app-angular-ticker',
  standalone: true,
  // Tailwind, like every other island here. Angular's own encapsulated styles would work too, but an
  // island is an ordinary part of the page and should reach the same design system the C# markup
  // around it uses.
  template: `
    <div class="flex flex-wrap items-center gap-3" data-testid="angular-ticker">
      <span class="rounded bg-slate-800 px-2 py-1 font-mono text-xs text-white dark:bg-slate-200 dark:text-slate-900">
        {{ symbol }}
      </span>

      <span class="text-sm text-slate-500 dark:text-slate-400">
        quote
        <strong class="tabular-nums text-slate-900 dark:text-slate-100" data-testid="angular-quote">
          {{ quote }}
        </strong>
      </span>

      <button
        type="button"
        class="cursor-pointer rounded border border-slate-300 px-2 py-1 text-sm transition-colors hover:bg-slate-100 dark:border-slate-600 dark:hover:bg-slate-800"
        data-testid="angular-refresh"
        (click)="refresh()"
      >
        refresh
      </button>

      <span class="text-sm text-slate-500 dark:text-slate-400">
        ticks
        <strong class="tabular-nums text-slate-900 dark:text-slate-100" data-testid="angular-ticks">
          {{ ticks }}
        </strong>
      </span>
    </div>
  `,
})
export class AngularTicker implements AngularTickerProps {
  @Input() symbol = ''
  @Input() quote = 0
  @Input() onRefreshRequested?: () => void

  // State Angular owns and C# never sees. Moving the quote from C# must not reset it.
  ticks = 0

  refresh() {
    this.ticks++
    this.onRefreshRequested?.()
  }
}

// The module default-exports the component class, the same contract React, Preact, Solid, Vue and
// Svelte islands follow. Lit is the one exception: it exports its registered tag NAME.
export default AngularTicker
