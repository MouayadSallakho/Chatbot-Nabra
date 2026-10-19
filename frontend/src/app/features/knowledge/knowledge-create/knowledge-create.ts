import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { CREATED_DOCUMENT_STATE, KNOWLEDGE_LIMITS } from '../knowledge-limits';
import { KnowledgeApi } from '../knowledge-api';

/**
 * Writing a knowledge document.
 *
 * <strong>Saving costs nothing.</strong> The document is stored and reported as
 * needing an index; turning it into vectors is a separate, deliberate action on
 * the editor. Indexing on save would mean every typo corrected in a long
 * document silently bought a fresh set of embeddings.
 *
 * <strong>Client-side validation is for immediacy only.</strong> Required, and a
 * length bound the reader can watch counting down. Everything else stays with
 * the Domain, and a rejected value is reported in the backend's own words.
 */
@Component({
  selector: 'app-knowledge-create',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmptyState, Icon, LocalNumberPipe],
  templateUrl: './knowledge-create.html',
  styleUrl: './knowledge-create.scss',
})
export class KnowledgeCreate {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  /**
   * Whether the reader may write a document.
   *
   * The list only offers this page to somebody who can; this is what answers
   * sensibly when they arrive by typing the URL instead.
   */
  protected readonly allowed = computed(() => this.permissions.has('knowledge.manage'));

  protected readonly limits = KNOWLEDGE_LIMITS;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  private readonly submitAttempted = signal(false);

  protected readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(KNOWLEDGE_LIMITS.titleMax)],
    }),
    content: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(KNOWLEDGE_LIMITS.contentMax)],
    }),
  });

  protected readonly titleLength = signal(0);
  protected readonly contentLength = signal(0);

  protected readonly contentRemaining = computed(
    () => KNOWLEDGE_LIMITS.contentMax - this.contentLength(),
  );

  constructor() {
    this.form.controls.title.valueChanges.subscribe((value) => this.titleLength.set(value.length));
    this.form.controls.content.valueChanges.subscribe((value) =>
      this.contentLength.set(value.length),
    );
  }

  protected showError(field: 'title' | 'content'): boolean {
    const control = this.form.controls[field];

    return control.invalid && (control.touched || this.submitAttempted());
  }

  /** Takes the reader to the first field that needs attention. */
  private focusFirstInvalid(): void {
    const id = this.form.controls.title.invalid ? 'knowledge-title' : 'knowledge-content';

    globalThis.document?.getElementById(id)?.focus();
  }

  protected async create(): Promise<void> {
    this.submitAttempted.set(true);

    // Also the double-submit guard: without it a second click before the first
    // response writes a second document.
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();

      return;
    }

    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    try {
      const created = await firstValueFrom(
        this.api.create(tenantId, {
          title: this.form.controls.title.value.trim(),
          content: this.form.controls.content.value,
        }),
      );

      this.toasts.success(
        $localize`:@@knowledge.toast.savedNeedsIndex:“${created.value.title}:title:” saved. It still needs an index.`,
      );

      // Handed to the editor through navigation state so it can open on
      // canonical server state without a second request. If the handoff is ever
      // missing the editor simply reads the document itself.
      await this.router.navigate(['/knowledge', created.value.id], {
        state: { [CREATED_DOCUMENT_STATE]: created },
      });
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }
}
