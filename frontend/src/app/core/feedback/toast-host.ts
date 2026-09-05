import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import { ToastService } from './toast-service';

/**
 * Where transient messages appear.
 *
 * Rendered once, by the shell. The live region is the reason this is a component
 * rather than markup repeated per feature: it has to exist in the DOM *before*
 * anything is announced, because assistive technology watches a region for
 * changes and will not announce a region that appears already populated.
 *
 * `role="status"` rather than `role="alert"`: these are confirmations of
 * completed actions, and `alert` interrupts whatever a screen reader is
 * currently saying. `aria-live="polite"` waits for a pause, which is right for
 * "Name saved".
 */
@Component({
  selector: 'app-toast-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="toast-host" role="status" aria-live="polite" aria-atomic="false">
      @for (toast of toasts(); track toast.id) {
        <div class="toast" [class.toast-error]="toast.tone === 'error'">
          <span class="toast-text">{{ toast.text }}</span>
          <button
            type="button"
            class="btn btn-ghost btn-icon btn-sm toast-dismiss"
            (click)="dismiss(toast.id)"
          >
            <app-icon name="close" [strokeWidth]="1.75" />
            <span class="visually-hidden">Dismiss</span>
          </button>
        </div>
      }
    </div>
  `,
  styleUrl: './toast-host.scss',
})
export class ToastHost {
  private readonly service = inject(ToastService);

  protected readonly toasts = this.service.toasts;

  protected dismiss(id: number): void {
    this.service.dismiss(id);
  }
}
