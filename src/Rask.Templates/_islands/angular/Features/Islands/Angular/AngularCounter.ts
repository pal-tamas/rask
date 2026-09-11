// An ordinary standalone Angular component, compiled by @analogjs/vite-plugin-angular.
import { Component, Input, signal } from '@angular/core'
import type { AngularCounterProps } from '@rask/AngularCounter.props'

@Component({
  selector: 'angular-counter',
  standalone: true,
  template: `
    <div class="island-counter" data-testid="angular-counter">
      <div class="caption">{{ caption }}</div>
      <button type="button" data-testid="angular-counter-add" (click)="add()">add {{ step }}</button>
      <span class="total">
        total <strong data-testid="angular-counter-total">{{ total() }}</strong>
      </span>
    </div>
  `,
})
export default class AngularCounter implements AngularCounterProps {
  @Input() step = 1
  @Input() caption = ''
  @Input() onTotalChanged?: (total: number) => void

  readonly total = signal(0)

  add() {
    this.total.update(t => t + this.step)
    this.onTotalChanged?.(this.total())
  }
}
