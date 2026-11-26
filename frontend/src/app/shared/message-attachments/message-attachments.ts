import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';

@Component({
  selector: 'app-message-attachments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (kind of items(); track $index) {
      @if (kind === 'SampleImage' && !imageFailed()) {
        <img
          src="/media/parcel-label.png"
          width="480"
          height="260"
          alt="Sample parcel label"
          i18n-alt="@@media.parcelAlt"
          (error)="imageFailed.set(true)"
        />
      } @else if (kind.startsWith('data:image/jpeg;base64,') && !failedUploads().has($index)) {
        <img
          [src]="kind"
          alt="Image sent by the customer"
          i18n-alt="@@media.customerAlt"
          (error)="markFailed($index)"
        />
      } @else if (kind === 'Unsupported') {
        <span i18n="@@media.unsupported">This attachment type has no preview.</span>
      } @else {
        <span i18n="@@media.unavailable">The image preview is unavailable.</span>
      }
    }
  `,
  styles: `
    img {
      display: block;
      max-inline-size: 100%;
      inline-size: 16rem;
      block-size: auto;
      border-radius: 0.5rem;
    }
    span {
      display: block;
      font-size: 0.875rem;
    }
    :host {
      display: grid;
      gap: 0.35rem;
    }
  `,
})
export class MessageAttachments {
  readonly items = input.required<readonly string[]>();
  protected readonly imageFailed = signal(false);
  protected readonly failedUploads = signal(new Set<number>());
  protected markFailed(index: number): void {
    this.failedUploads.update((previous) => new Set(previous).add(index));
  }
}
