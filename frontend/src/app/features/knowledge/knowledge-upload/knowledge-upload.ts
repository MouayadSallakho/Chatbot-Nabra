import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  KNOWLEDGE_FILE_FORMATS,
  KNOWLEDGE_FILE_LIMITS,
  KNOWLEDGE_LIMITS,
  UPLOADED_DOCUMENT_STATE,
} from '../knowledge-limits';
import { KnowledgeApi } from '../knowledge-api';

/**
 * Uploading a business file into Knowledge.
 *
 * <strong>Uploading extracts text. It does not index and it does not store the
 * file.</strong> Both are said on the page rather than left to be discovered:
 * somebody who uploads a policy and expects an agent to answer from it
 * immediately has been misled, and somebody who uploads their only copy of a
 * document expecting to download it later has been misled far worse.
 *
 * <strong>A native file input, and no library.</strong> Drag-and-drop would mean
 * a dependency, a drop target that has to be reachable by keyboard anyway, and a
 * second code path to the same request. The input a browser already ships is
 * focusable, labelled, announced and works on a phone.
 *
 * <strong>The checks here are courtesy, not enforcement.</strong> Size and
 * extension are looked at so somebody learns before waiting for an upload rather
 * than after; the backend refuses on its own terms regardless, reading what is
 * actually inside the file rather than what its name claims.
 */
@Component({
  selector: 'app-knowledge-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, Icon],
  templateUrl: './knowledge-upload.html',
  styleUrl: './knowledge-upload.scss',
})
export class KnowledgeUpload {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  /** Answers sensibly for somebody who arrived by typing the URL. */
  protected readonly allowed = computed(() => this.permissions.has('knowledge.manage'));

  protected readonly limits = KNOWLEDGE_LIMITS;
  protected readonly fileLimits = KNOWLEDGE_FILE_LIMITS;
  protected readonly formats = KNOWLEDGE_FILE_FORMATS;

  /** The chosen file, or null while nothing is chosen. */
  protected readonly chosen = signal<File | null>(null);

  /**
   * A reason this file was refused before it was ever sent.
   *
   * Kept apart from {@link failure} because the repairs differ: this one is
   * fixed by choosing a different file, while a server refusal may be about the
   * file's insides and is worth reading in the server's own words.
   */
  protected readonly localRefusal = signal<string | null>(null);

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

  /** The chosen file's size, in the words a person uses for it. */
  protected readonly chosenSize = computed(() => {
    const file = this.chosen();

    return file ? describeBytes(file.size) : '';
  });

  /**
   * Takes the file the browser reports, and says straight away if it is hopeless.
   *
   * An empty selection -- which is what cancelling the picker produces -- clears
   * the choice rather than leaving a stale filename on screen next to a button
   * that would upload something else.
   */
  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;

    this.failure.set(null);
    this.localRefusal.set(null);
    this.chosen.set(file);

    if (!file) {
      return;
    }

    if (file.size === 0) {
      this.localRefusal.set('That file is empty. Choose a file with something in it.');

      return;
    }

    if (file.size > KNOWLEDGE_FILE_LIMITS.maxBytes) {
      this.localRefusal.set(
        `That file is ${describeBytes(file.size)}, and uploads must be ` +
          `${KNOWLEDGE_FILE_LIMITS.maxMegabytes} MB or smaller. Split it into smaller ` +
          'documents, or upload the part that matters.',
      );

      return;
    }

    if (!hasSupportedExtension(file.name)) {
      this.localRefusal.set(
        'That does not look like a PDF, Word document, text file or CSV. Choose one of ' +
          'those formats.',
      );
    }
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

      this.toasts.success(`${created.value.title} saved. It still needs indexing.`);

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
}

/** The extensions the picker suggests, checked again after the fact. */
const SUPPORTED_EXTENSIONS = ['.pdf', '.docx', '.txt', '.csv'];

function hasSupportedExtension(name: string): boolean {
  const lowered = name.toLowerCase();

  return SUPPORTED_EXTENSIONS.some((extension) => lowered.endsWith(extension));
}

/**
 * A file size somebody can read.
 *
 * Whole kilobytes below a megabyte and one decimal above it: "1.4 MB" is what a
 * person compares against a 10 MB limit, and "1468006 bytes" is not.
 */
export function describeBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} bytes`;
  }

  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
