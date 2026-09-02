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
  template: `
    <div class="angular-ticker" data-testid="angular-ticker">
      <div class="caption">{{ symbol }}</div>

      <span class="quote">
        quote <strong data-testid="angular-quote">{{ quote }}</strong>
      </span>

      <button type="button" data-testid="angular-refresh" (click)="refresh()">refresh</button>

      <span class="ticks">
        ticks <strong data-testid="angular-ticks">{{ ticks }}</strong>
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
