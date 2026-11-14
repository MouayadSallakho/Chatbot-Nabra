import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';

/**
 * The conversation pane when nothing is open.
 *
 * The second panel's content while nothing is open (Calm Glass, 39.4a): on the
 * panel colour, with the empty state's one serif line and one sentence.
 *
 * Seen only where the list sits beside it (1280px and wider); on a narrower
 * screen `/conversations` is the list alone. Says what belongs here and how to
 * fill it, and nothing else: no counts, no illustration.
 */
@Component({
  selector: 'app-inbox-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="inbox-placeholder">
      <span class="state-icon" aria-hidden="true">
        <app-icon name="conversations" [strokeWidth]="1.6" />
      </span>
      <p class="state-title" i18n="@@inbox.placeholder.title">Choose a conversation</p>
      <p class="state-description" i18n="@@inbox.placeholder.body">
        Its messages, who is answering and the controls to step in open here, beside the list.
      </p>
    </div>
  `,
  styles: `
    :host {
      display: grid;
      block-size: 100%;
      place-items: center;
      background-color: var(--color-panel);
    }

    /* Beside the list, a solid panel of its own (the thread draws its own). */
    :host-context(.inbox.is-panes) {
      border: 1px solid var(--color-line);
      border-radius: var(--radius-panel);
    }

    .inbox-placeholder {
      display: grid;
      justify-items: center;
      gap: var(--space-3);
      padding: var(--space-8);
      text-align: center;

      p {
        margin: 0;
      }
    }
  `,
})
export class InboxPlaceholder {}
