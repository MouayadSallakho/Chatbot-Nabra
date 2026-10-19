import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  KNOWLEDGE_FILE_LIMITS,
  KNOWLEDGE_LIMITS,
  UPLOADED_DOCUMENT_STATE,
} from '../knowledge-limits';
import { KnowledgeApi } from '../knowledge-api';
import {
  claimedFileType,
  fileRefusal,
  fileSize,
  maxSizeLabel,
  uploadFailure,
} from '../knowledge-file';
import { acceptedFormats, knowledgeTypeWord } from '../knowledge-words';

/**
 * Uploading a business file into Knowledge.
 *
 * <strong>Uploading extracts text. It does not index and it does not store the
 * file.</strong> Both are said on the page rather than left to be discovered.
 *
 * <strong>A drop zone with a real button inside it</strong> (Phase 36.8).
 * Dropping a file is a shortcut for a pointer; "Choose a file" is a button that
 * opens the browser's own picker, so keyboards, screen readers and phones have
 * the same path. Both lead to one function and the same checks. The native
 * input stays in the page (visually hidden, out of the tab order) because the
 * picker is what it is for. No library: the platform's drag events are enough.
 *
 * <strong>The checks here are courtesy, not enforcement.</strong> Size and
 * extension are looked at so somebody learns before waiting for an upload; the
 * backend refuses on its own terms regardless, reading what is actually inside
 * the file rather than what its name claims.
 */
@Component({
  selector: 'app-knowledge-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmptyState, Icon],
  templateUrl: './knowledge-upload.html',
  styleUrl: './knowledge-upload.scss',
})
export class KnowledgeUpload {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  private readonly format = inject(LocaleFormat);

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private readonly chooseButton = viewChild<ElementRef<HTMLButtonElement>>('chooseButton');

  /** Answers sensibly for somebody who arrived by typing the URL. */
  protected readonly allowed = computed(() => this.permissions.has('knowledge.manage'));

  protected readonly limits = KNOWLEDGE_LIMITS;
  protected readonly fileLimits = KNOWLEDGE_FILE_LIMITS;
  protected readonly formats = acceptedFormats();
  protected readonly maxSize = maxSizeLabel(this.format.language);

  /** The chosen file, or null while nothing is chosen. */
  protected readonly chosen = signal<File | null>(null);

  /**
   * A reason this file was refused before it was ever sent.
   *
   * Kept apart from {@link failure} because the repairs differ: this one is
   * fixed by choosing a different file, while a server refusal may be about the
   * file's insides.
   */
  protected readonly localRefusal = signal<string | null>(null);

  /** More than one file was dropped; only the first was taken, and the page says so. */
  protected readonly extraFiles = signal(false);

  /** A pointer is dragging something over the drop zone. */
  protected readonly dragging = signal(false);

  /** How many drag-enter events are open, so leaving a child does not end the drag. */
  private dragDepth = 0;

  protected readonly uploading = signal(false);
  protected readonly failure = signal<ApiError | null>(null);

  protected readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(KNOWLEDGE_LIMITS.titleMax)],
    }),
  });

  /** Whether pressing Upload would do anything. */
  protected readonly canUpload = computed(
    () => this.chosen() !== null && this.localRefusal() === null && !this.uploading(),
  );

  /** The chosen file's size, in the reader's words. */
  protected readonly chosenSize = computed(() => {
    const file = this.chosen();

    return file ? fileSize(file.size, this.format.language) : '';
  });

  /** The chosen file's format as its name claims it, or null for one not accepted. */
  protected readonly chosenType = computed(() => {
    const file = this.chosen();
    const type = file ? claimedFileType(file.name) : null;

    return type ? knowledgeTypeWord(type) : null;
  });

  /** The server's refusal, in the reader's language. */
  protected readonly failureMessage = computed(() => {
    const error = this.failure();

    return error ? uploadFailure(error, this.format.language) : null;
  });

  /** Opens the browser's file picker: the keyboard path into the drop zone. */
  protected openPicker(): void {
    this.fileInput()?.nativeElement.click();
  }

  /**
   * Takes the file the picker reports.
   *
   * An empty selection, which is what cancelling the picker produces, clears the
   * choice rather than leaving a stale filename next to a button that would
   * upload something else.
   */
  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;

    this.take(input.files?.item(0) ?? null);
  }

  protected dragEnter(event: DragEvent): void {
    if (!this.carriesFiles(event)) {
      return;
    }

    event.preventDefault();
    this.dragDepth++;
    this.dragging.set(true);
  }

  protected dragOver(event: DragEvent): void {
    if (!this.carriesFiles(event)) {
      return;
    }

    // Without this the browser opens the dropped file instead of handing it over.
    event.preventDefault();

    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = this.uploading() ? 'none' : 'copy';
    }
  }

  protected dragLeave(): void {
    this.dragDepth = Math.max(0, this.dragDepth - 1);

    if (this.dragDepth === 0) {
      this.dragging.set(false);
    }
  }

  /** One file at a time: the first is taken, and the page says if more were dropped. */
  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth = 0;
    this.dragging.set(false);

    if (this.uploading()) {
      return;
    }

    const files = event.dataTransfer?.files;
    const file = files?.item(0) ?? null;

    this.take(file);

    this.extraFiles.set(file !== null && files !== undefined && files.length > 1);
  }

  /** Forgets the chosen file and returns focus to the button that chooses one. */
  protected removeChosen(): void {
    this.take(null);

    const input = this.fileInput()?.nativeElement;

    if (input) {
      input.value = '';
    }

    this.chooseButton()?.nativeElement.focus();
  }

  protected async upload(): Promise<void> {
    const file = this.chosen();
    const tenantId = this.workspace.tenantId();

    // Also the double-submit guard: without it a second click before the first
    // response creates a second document from the same file.
    if (!file || !tenantId || !this.canUpload() || this.form.invalid) {
      return;
    }

    this.uploading.set(true);
    this.failure.set(null);

    try {
      const created = await firstValueFrom(
        this.api.uploadFile(tenantId, file, this.form.controls.title.value),
      );

      this.toasts.success(
        $localize`:@@knowledge.toast.savedNeedsIndex:“${created.value.title}:title:” saved. It still needs an index.`,
      );

      // Handed to the editor through navigation state, exactly as the create
      // page does: the response already carries the document and its tag.
      await this.router.navigate(['/knowledge', created.value.id], {
        state: { [UPLOADED_DOCUMENT_STATE]: created },
      });
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.uploading.set(false);
    }
  }

  private take(file: File | null): void {
    this.failure.set(null);
    this.extraFiles.set(false);
    this.chosen.set(file);
    this.localRefusal.set(file ? fileRefusal(file, this.format.language) : null);
  }

  /** Only a drag that carries files is ours; dragged text is left alone. */
  private carriesFiles(event: DragEvent): boolean {
    return Array.from(event.dataTransfer?.types ?? []).includes('Files');
  }
}
