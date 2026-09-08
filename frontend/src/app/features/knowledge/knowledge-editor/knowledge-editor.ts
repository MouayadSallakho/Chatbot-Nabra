import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { Versioned } from '@app/core/api/versioned';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { CREATED_DOCUMENT_STATE, KNOWLEDGE_LIMITS } from '../knowledge-limits';
import { KnowledgeApi, KnowledgeDocument } from '../knowledge-api';

/** Which action ran into a version that is no longer current. */
type ConflictSource = 'content' | 'index' | 'availability';

/**
 * The knowledge editor: the text, its index, whether it is in use, and what it
 * would retrieve.
 *
 * <strong>Entity tag state is the heart of this screen</strong>, exactly as it is
 * on the agent editor. The tag is read from the `ETag` header when the document
 * loads, kept in a signal, sent as `If-Match` on every mutation -- indexing
 * included, because indexing replaces the chunk set and moves the version -- and
 * replaced with the new tag from each successful response. It is never shown,
 * edited, parsed or constructed here.
 *
 * <strong>What happens on 412 is deliberate.</strong> The write is refused, the
 * reader's text stays exactly where it is, and nothing is retried, merged or
 * overwritten. They are told what happened and offered a reload -- which warns
 * first if it would discard their work. Silently replacing the editor with server
 * state would destroy the thing they were in the middle of writing, which is
 * precisely the loss the precondition exists to prevent.
 *
 * <strong>Indexing is the only control here that spends money</strong>, and it
 * says so before it is pressed and reports afterwards what it actually cost.
 */
@Component({
  selector: 'app-knowledge-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, DatePipe, PageHeader, EmptyState, Icon],
  templateUrl: './knowledge-editor.html',
  styleUrl: './knowledge-editor.scss',
})
export class KnowledgeEditor implements OnInit {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly toasts = inject(ToastService);
  private readonly router = inject(Router);

  /** Bound from the route through `withComponentInputBinding`. */
  readonly knowledgeDocumentId = input.required<string>();

  protected readonly limits = KNOWLEDGE_LIMITS;

  protected readonly loading = signal(true);
  protected readonly loadFailure = signal<ApiError | null>(null);

  /** The canonical document, exactly as the server last described it. */
  protected readonly document = signal<KnowledgeDocument | null>(null);

  /**
   * The version every mutation is checked against.
   *
   * Null means this screen has nothing safe to write with. Rather than sending an
   * unconditional mutation -- which the backend would refuse with 428 anyway --
   * the save controls are blocked and a reload is offered.
   */
  protected readonly etag = signal<string | null>(null);

  protected readonly saving = signal(false);
  protected readonly indexing = signal(false);
  protected readonly changingAvailability = signal(false);

  protected readonly conflict = signal<ConflictSource | null>(null);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly indexFailure = signal<ApiError | null>(null);
  protected readonly availabilityFailure = signal<ApiError | null>(null);

  /** What the last index attempt actually did, for an honest report. */
  protected readonly lastIndex = signal<{ chunks: number; calls: number; current: boolean } | null>(
    null,
  );

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

  /** Live values, so dirty state and counters are derived rather than tracked. */
  private readonly titleValue = signal('');
  private readonly contentValue = signal('');

  protected readonly titleChanged = computed(
    () =>
      this.titleValue().trim() !== (this.document()?.title ?? '') &&
      this.titleValue().trim() !== '',
  );

  protected readonly contentChanged = computed(
    () => this.contentValue() !== (this.document()?.content ?? ''),
  );

  /** Any unsaved edit at all, for the reload warning. */
  protected readonly dirty = computed(() => this.titleChanged() || this.contentChanged());

  protected readonly contentRemaining = computed(
    () => KNOWLEDGE_LIMITS.contentMax - this.contentValue().length,
  );

  /**
   * Whether the reader may change this document.
   *
   * Without it the fields become read-only rather than disappearing: somebody
   * with `knowledge.view` is entitled to read a document, and hiding the text
   * they came to read would be worse than showing it plainly.
   */
  protected readonly canManage = computed(() => this.permissions.has('knowledge.manage'));

  /**
   * Whether a conditional write is possible.
   *
   * Two independent reasons it might not be: there is no entity tag to present,
   * or the reader does not hold the managing permission. Both block the same
   * controls, and the template distinguishes them, since "not yet loaded" and
   * "not yours to change" need different words.
   */
  protected readonly cannotWrite = computed(() => this.etag() === null || !this.canManage());

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  /** Whether indexing this document now would actually do anything. */
  protected readonly indexWouldWork = computed(
    () => this.document()?.readiness !== 'Ready' || this.contentChanged(),
  );

  /**
   * A just-written document, handed over by the create page.
   *
   * Read in the constructor because that is the only moment it is available:
   * `currentNavigation()` is null once the navigation has finished.
   */
  private readonly handedOver = this.router.currentNavigation()?.extras.state?.[
    CREATED_DOCUMENT_STATE
  ] as Versioned<KnowledgeDocument> | undefined;

  constructor() {
    this.form.controls.title.valueChanges.subscribe((value) => this.titleValue.set(value));
    this.form.controls.content.valueChanges.subscribe((value) => this.contentValue.set(value));
  }

  ngOnInit(): void {
    if (this.handedOver) {
      this.adopt(this.handedOver);
      this.loading.set(false);

      return;
    }

    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailure.set(null);

    try {
      this.adopt(await firstValueFrom(this.api.get(this.tenantId(), this.knowledgeDocumentId())));
    } catch (error) {
      this.loadFailure.set(toApiError(error));
      this.document.set(null);
      this.etag.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  // ------------------------------------------------------------------ content

  protected async save(): Promise<void> {
    // No request when nothing changed. A save that rewrote the same values would
    // still move the version, which would make somebody else's open editor stale
    // for no reason at all.
    if (!this.dirty() || this.form.invalid || this.saving() || this.cannotWrite()) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.conflict.set(null);
    this.lastIndex.set(null);

    try {
      const previous = this.document()?.content;

      this.adopt(
        await firstValueFrom(
          this.api.update(
            this.tenantId(),
            this.knowledgeDocumentId(),
            {
              title: this.titleValue().trim(),
              content: this.contentValue(),
            },
            this.etag() as string,
          ),
        ),
      );

      this.toasts.success(
        previous !== this.document()?.content
          ? 'Saved. The content changed, so this document needs indexing again.'
          : 'Saved.',
      );
    } catch (error) {
      this.handleFailure(error, 'content');
    } finally {
      this.saving.set(false);
    }
  }

  protected reset(): void {
    const current = this.document();

    if (!current) {
      return;
    }

    this.form.setValue({ title: current.title, content: current.content });
    this.saveFailure.set(null);
  }

  // -------------------------------------------------------------------- index

  /**
   * Rebuilds the vectors behind this document.
   *
   * Unsaved text is refused rather than quietly included: indexing embeds what
   * the server holds, so indexing while the editor holds different text would
   * produce an index of something the reader is not looking at.
   */
  protected async index(): Promise<void> {
    if (this.indexing() || this.cannotWrite()) {
      return;
    }

    if (this.dirty()) {
      this.indexFailure.set({
        kind: 'validation',
        status: 0,
        message: 'Save your changes first — indexing works from the saved version.',
      });

      return;
    }

    this.indexing.set(true);
    this.indexFailure.set(null);
    this.conflict.set(null);
    this.lastIndex.set(null);

    try {
      const result = await firstValueFrom(
        this.api.index(this.tenantId(), this.knowledgeDocumentId(), this.etag() as string),
      );

      this.document.set(result.value.document);
      this.etag.set(result.etag);
      this.syncFields(result.value.document);

      this.lastIndex.set({
        chunks: result.value.chunkCount,
        calls: result.value.embeddingCalls,
        current: result.value.alreadyCurrent,
      });

      this.toasts.success(
        result.value.alreadyCurrent
          ? 'Already up to date. Nothing was sent to the AI provider.'
          : `Indexed into ${result.value.chunkCount} pieces.`,
      );
    } catch (error) {
      this.handleFailure(error, 'index');
    } finally {
      this.indexing.set(false);
    }
  }

  // ------------------------------------------------------------- availability

  protected async setAvailability(enabled: boolean): Promise<void> {
    if (this.changingAvailability() || this.cannotWrite()) {
      return;
    }

    this.changingAvailability.set(true);
    this.availabilityFailure.set(null);
    this.conflict.set(null);

    try {
      const tenantId = this.tenantId();
      const id = this.knowledgeDocumentId();
      const etag = this.etag() as string;

      this.adopt(
        await firstValueFrom(
          enabled ? this.api.enable(tenantId, id, etag) : this.api.disable(tenantId, id, etag),
        ),
      );

      this.toasts.success(
        enabled
          ? 'Back in use. Agents assigned this document can draw on it again.'
          : 'Taken out of use. No agent will draw on it until you switch it back on.',
      );
    } catch (error) {
      this.handleFailure(error, 'availability');
    } finally {
      this.changingAvailability.set(false);
    }
  }

  // ----------------------------------------------------------------- conflict

  /**
   * Reloads the server's current state, after asking if that would cost work.
   *
   * The confirmation is the point: the reader's unsaved text is still on screen
   * after a 412, and replacing it is their decision to make.
   */
  protected async reloadLatest(): Promise<void> {
    if (this.dirty() && !this.confirmReplace()) {
      return;
    }

    this.conflict.set(null);
    this.saveFailure.set(null);
    this.indexFailure.set(null);
    this.availabilityFailure.set(null);

    await this.load();
  }

  /** Deliberately overridable in tests -- the browser's own confirm cannot be driven from one. */
  protected confirmReplace(): boolean {
    return globalThis.confirm(
      'Reloading replaces your unsaved changes with the latest saved version. Continue?',
    );
  }

  /**
   * Sorts a failed write into the things it can mean.
   *
   * A stale version and everything else lead to different repairs, so they are
   * never collapsed into one "save failed" message.
   */
  private handleFailure(failure: unknown, source: ConflictSource): void {
    const error = toApiError(failure);

    // 412 and 428 both mean "this screen's version is not usable". The tag is
    // dropped so no further mutation can be attempted with it, which is what
    // forces a deliberate reload rather than a retry loop.
    if (error.kind === 'stale' || error.kind === 'preconditionRequired') {
      this.conflict.set(source);
      this.etag.set(null);

      return;
    }

    if (source === 'content') {
      this.saveFailure.set(error);
    } else if (source === 'index') {
      this.indexFailure.set(error);
    } else {
      this.availabilityFailure.set(error);
    }
  }

  /** Takes a server representation as the new canonical state. */
  private adopt(versioned: Versioned<KnowledgeDocument>): void {
    this.document.set(versioned.value);
    this.etag.set(versioned.etag);

    this.syncFields(versioned.value);
  }

  private syncFields(document: KnowledgeDocument): void {
    this.form.controls.title.setValue(document.title, { emitEvent: false });
    this.titleValue.set(document.title);

    this.form.controls.content.setValue(document.content, { emitEvent: false });
    this.contentValue.set(document.content);
  }
}
