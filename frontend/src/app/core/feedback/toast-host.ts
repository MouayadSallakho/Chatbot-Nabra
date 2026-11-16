import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
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
  imports: [Icon, NgTemplateOutlet],
  template: `
    <!--
      Two live regions, so each kind of message is announced as what it is: a
      confirmation politely, after whatever the reader is hearing; a failure at
      once. Hovering or focusing either one holds every toast on screen.
    -->
    <div
      class="toast-host"
      (mouseenter)="hold()"
      (mouseleave)="release()"
      (focusin)="hold()"
      (focusout)="release()"
    >
      <div class="toast-region" role="status" aria-live="polite" aria-atomic="false">
        @for (toast of successes(); track toast.id) {
          <ng-container
            [ngTemplateOutlet]="item"
            [ngTemplateOutletContext]="{ $implicit: toast }"
          />
        }
      </div>
      <div class="toast-region" role="alert" aria-atomic="false">
        @for (toast of errors(); track toast.id) {
          <ng-container
            [ngTemplateOutlet]="item"
            [ngTemplateOutletContext]="{ $implicit: toast }"
          />
        }
      </div>
    </div>

    <ng-template #item let-toast>
      <div class="toast" [class.toast-error]="toast.tone === 'error'">
        <app-icon
          class="toast-icon"
          [name]="toast.tone === 'error' ? 'alert' : 'check'"
          [strokeWidth]="1.75"
        />
        <span class="toast-text">{{ toast.text }}</span>
        <button
          type="button"
          class="btn btn-quiet btn-icon btn-sm toast-dismiss"
          (click)="dismiss(toast.id)"
        >
          <app-icon name="close" [strokeWidth]="1.75" />
          <span class="visually-hidden" i18n="@@toast.dismiss">Dismiss</span>
        </button>
      </div>
    </ng-template>
  `,
  styleUrl: './toast-host.scss',
})
export class ToastHost {
  private readonly service = inject(ToastService);

  protected readonly successes = computed(() =>
    this.service.toasts().filter((toast) => toast.tone !== 'error'),
  );

  protected readonly errors = computed(() =>
    this.service.toasts().filter((toast) => toast.tone === 'error'),
  );

  protected dismiss(id: number): void {
    this.service.dismiss(id);
  }

  protected hold(): void {
    this.service.hold();
  }

  protected release(): void {
    this.service.release();
  }
}
