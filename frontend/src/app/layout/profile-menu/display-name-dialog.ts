import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { ToastService } from '@app/core/feedback/toast-service';
import { LanguageService } from '@app/core/i18n/language-service';
import { registerProblemWords } from '@app/core/problems/problem-words';
import {
  DISPLAY_NAME_CONTROL_CHARACTERS,
  DISPLAY_NAME_LIMIT,
  DISPLAY_NAME_PROBLEMS,
  displayNameTooLong,
  hasControlCharacters,
  normaliseDisplayName,
} from './display-name-problems';

/**
 * The member's own display name (Phase 39.6a, plan 3.6 and note 7): a small
 * dialog opened from the account menu.
 *
 * <strong>Their own name, nobody else's.</strong> `PUT /api/me/display-name`
 * has no user id: it edits the signed-in account, in every workspace it belongs
 * to, with no permission involved (like the language). Last write wins; there is
 * no version to send. The name is read back from `GET /api/auth/session` on the
 * next page load; until then the screen shows what the server keeps (trimmed,
 * inner spaces made one), which `normaliseDisplayName` works out the same way.
 *
 * <strong>Staff-facing only.</strong> Colleagues see it beside the address
 * (team, inbox, transcript); a visitor still reads "Support" (owner, Q5).
 *
 * <strong>Refusals by code.</strong> The two rules (80 characters, no hidden
 * characters) are checked here first, in the server's own order and words, so
 * nobody waits for a round trip to learn them; a refusal from the server is
 * worded by its `code` all the same (registered when this dialog loads).
 *
 * Loaded on demand (`@defer` in the account menu), so the startup JavaScript
 * does not carry it. The native `<dialog>` gives the focus trap, Escape and the
 * top layer; closing returns focus to the account menu's button, which had it
 * when the dialog opened. Glass with a scrim, as every dialog (calm-glass §6).
 */
@Component({
  selector: 'app-display-name-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #dialog
      class="confirm display-name-dialog"
      aria-labelledby="display-name-title"
      aria-describedby="display-name-lede"
      data-testid="display-name-dialog"
      (close)="onNativeClose()"
      (click)="onDialogClick($event)"
    >
      @if (isOpen()) {
        <form (submit)="save($event)" novalidate>
          <h2 id="display-name-title" class="text-section-title" i18n="@@account.displayName.title">
            Your display name
          </h2>
          <p
            id="display-name-lede"
            class="confirm-body text-small text-secondary"
            i18n="@@account.displayName.lede"
          >
            How your team sees you. Customers never see it: they read “Support”.
          </p>

          <div class="field display-name-field">
            <label class="form-label" for="display-name-input" i18n="@@account.displayName.label"
              >Display name</label
            >
            <input
              #field
              id="display-name-input"
              class="form-control"
              type="text"
              dir="auto"
              autocomplete="name"
              spellcheck="false"
              [value]="draft()"
              (input)="onInput($event)"
              [attr.aria-invalid]="problem() ? 'true' : null"
              [attr.aria-describedby]="
                problem() ? 'display-name-problem display-name-hint' : 'display-name-hint'
              "
              data-testid="display-name-input"
            />
            <p id="display-name-hint" class="form-hint" i18n="@@account.displayName.hint">
              Leave it empty to show your email address instead.
            </p>
            @if (problem(); as words) {
              <p
                id="display-name-problem"
                class="form-error"
                role="alert"
                data-testid="display-name-problem"
              >
                {{ words }}
              </p>
            }
          </div>

          <div class="confirm-actions">
            <button type="button" class="btn btn-secondary" (click)="close()">
              {{ cancelLabel }}
            </button>
            <button
              type="submit"
              class="btn btn-primary"
              [class.btn-loading]="saving()"
              [attr.aria-disabled]="saving() ? 'true' : null"
              data-testid="save-display-name"
            >
              <span class="btn-label" i18n="@@account.displayName.save">Save name</span>
              @if (saving()) {
                <span class="spinner" aria-hidden="true"></span>
              }
            </button>
          </div>
        </form>
      }
    </dialog>
  `,
  styleUrl: '../../core/feedback/confirm-dialog.scss',
  styles: `
    .display-name-field {
      margin-block-start: var(--space-5);
    }
  `,
})
export class DisplayNameDialog {
  /** The name the account has now; null when none was chosen. */
  readonly current = input<string | null>(null);

  /**
   * How many times the reader has asked for the dialog. Each new count opens
   * it, including the first, which is the one that loaded it.
   */
  readonly requests = input(0);

  private readonly auth = inject(AuthService);
  private readonly toasts = inject(ToastService);
  private readonly language = inject(LanguageService);
  private readonly injector = inject(Injector);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');

  protected readonly cancelLabel = $localize`:@@dialog.cancel:Cancel`;

  protected readonly isOpen = signal(false);
  protected readonly draft = signal('');
  protected readonly saving = signal(false);
  protected readonly problem = signal<string | null>(null);

  constructor() {
    registerProblemWords(DISPLAY_NAME_PROBLEMS);

    effect(() => {
      if (this.requests() > 0) {
        untracked(() => this.open());
      }
    });
  }

  /** Opens the dialog on the saved name, with the cursor in the field. */
  open(): void {
    if (this.isOpen()) {
      return;
    }

    this.draft.set(this.current() ?? '');
    this.problem.set(null);
    this.isOpen.set(true);
    this.dialog().nativeElement.showModal();

    afterNextRender(() => this.field()?.nativeElement.select(), { injector: this.injector });
  }

  protected close(): void {
    if (!this.saving()) {
      this.dialog().nativeElement.close();
    }
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value);
    // The rule broken is no longer the text on screen: say it again on save.
    this.problem.set(null);
  }

  /** Saves, after the two rules the server would refuse. */
  protected async save(event: Event): Promise<void> {
    event.preventDefault();

    if (this.saving()) {
      return;
    }

    const name = normaliseDisplayName(this.draft());
    const refusal = this.check(name);

    if (refusal !== null) {
      this.refuse(refusal);

      return;
    }

    if (name === (normaliseDisplayName(this.current() ?? '') ?? null)) {
      this.close();

      return;
    }

    this.saving.set(true);
    this.problem.set(null);

    try {
      await this.auth.saveDisplayName(name);

      this.saving.set(false);
      this.close();
      this.toasts.success(
        name === null
          ? $localize`:@@account.displayName.removed:Your display name is removed. Your team sees your email address.`
          : $localize`:@@account.displayName.saved:Your display name is saved.`,
      );
    } catch (error: unknown) {
      this.saving.set(false);
      // The typed name stays: somebody refused has not stopped wanting it.
      this.refuse(toApiError(error).message);
    }
  }

  /** Escape, Cancel and the backdrop all close it without saving. */
  protected onNativeClose(): void {
    this.isOpen.set(false);
    this.problem.set(null);
  }

  protected onDialogClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close();
    }
  }

  private check(name: string | null): string | null {
    if (name === null) {
      return null;
    }

    if (name.length > DISPLAY_NAME_LIMIT) {
      return displayNameTooLong(this.language.current);
    }

    return hasControlCharacters(name) ? DISPLAY_NAME_CONTROL_CHARACTERS : null;
  }

  private refuse(words: string): void {
    this.problem.set(words);
    afterNextRender(() => this.field()?.nativeElement.focus(), { injector: this.injector });
  }
}
