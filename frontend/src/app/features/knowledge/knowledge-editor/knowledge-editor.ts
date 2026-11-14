import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  HasUnsavedChanges,
  protectUnsavedChanges,
  RELOAD_REPLACES_CHANGES_QUESTION,
} from '@app/core/navigation/unsaved-changes';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { KnowledgeRealtime } from '@app/core/realtime/knowledge-realtime';
import { Versioned } from '@app/core/api/versioned';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { Chip } from '@app/shared/chip/chip';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import {
  CREATED_DOCUMENT_STATE,
  KNOWLEDGE_FILE_LIMITS,
  KNOWLEDGE_LIMITS,
  UPLOADED_DOCUMENT_STATE,
} from '../knowledge-limits';
import { KnowledgeApi, KnowledgeDocument, KnowledgeIndexAttempt } from '../knowledge-api';
import { LocalDatePipe, LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import {
  claimedFileType,
  fileRefusal,
  fileSize,
  maxSizeLabel,
  uploadFailure,
} from '../knowledge-file';
import {
  acceptedFormats,
  characterCount,
  indexFailureReason,
  knowledgeDisplayState,
  knowledgeStateTone,
  knowledgeStateWord,
  knowledgeTypeWord,
} from '../knowledge-words';

/** Which action ran into a version that is no longer current. */
type ConflictSource = 'content' | 'index' | 'availability' | 'source';

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
 *
 * <strong>Replacing the file is the same conditional write as any other.</strong>
 * A document that came from a file can be given a new one; the new text replaces
 * the old, which means the index no longer describes it and the document needs
 * indexing again. The page says so before the button is pressed rather than
 * reporting it afterwards. Replacing is offered only where there is a source to
 * replace: a written document is edited by typing, and uploading over one would
 * silently discard what somebody wrote.
 *
 * <strong>Layout (Phase 36.8)</strong>: the document on the main side; its
 * state, indexing, availability and source file beside it from 80rem. The
 * state is the API's (`Indexed`, `Needs index`, `Disabled`), plus what the
 * newest index attempt adds (Phase 38.3) and an index request that failed here.
 *
 * <strong>Indexing ends by itself (Phase 38.8, BA-11).</strong> The page hears
 * when an attempt on this document starts or settles, over the page's one
 * connection, and re-reads the document. An attempt no longer depends on the tab
 * that started it (BA-10), so a reload while indexing ends the same way. A live
 * read never takes the reader's unsaved text or the version it was written
 * against: with edits in the box only the state is updated, and a save still
 * meets the stale-version check.
 */
@Component({
  selector: 'app-knowledge-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    PageHeader,
    EmptyState,
    Icon,
    ConfirmDialog,
    LocalDatePipe,
    LocalNumberPipe,
    Chip,
  ],
  templateUrl: './knowledge-editor.html',
  styleUrl: './knowledge-editor.scss',
})
export class KnowledgeEditor implements OnInit, HasUnsavedChanges {
  private readonly disableDialog = viewChild.required<ConfirmDialog>('disableDialog');

  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly toasts = inject(ToastService);
  private readonly router = inject(Router);
  private readonly format = inject(LocaleFormat);
  private readonly realtime = inject(KnowledgeRealtime);
  private readonly destroyRef = inject(DestroyRef);

  /** One live re-read at a time; events meanwhile cause exactly one more. */
  private liveReading = false;
  private liveAgain = false;

  private readonly replaceInput = viewChild<ElementRef<HTMLInputElement>>('replaceInput');

  protected readonly stateWord = knowledgeStateWord;
  protected readonly stateTone = knowledgeStateTone;
  protected readonly typeWord = knowledgeTypeWord;
  protected readonly formats = acceptedFormats();
  protected readonly maxSize = maxSizeLabel(this.format.language);

  /** Bound from the route through `withComponentInputBinding`. */
  readonly knowledgeDocumentId = input.required<string>();

  protected readonly limits = KNOWLEDGE_LIMITS;
  protected readonly fileLimits = KNOWLEDGE_FILE_LIMITS;

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
  protected readonly replacing = signal(false);

  protected readonly conflict = signal<ConflictSource | null>(null);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly indexFailure = signal<ApiError | null>(null);
  protected readonly availabilityFailure = signal<ApiError | null>(null);
  protected readonly replaceFailure = signal<ApiError | null>(null);

  /** The replacement file, once one is chosen. */
  protected readonly replacement = signal<File | null>(null);

  /** A reason the chosen replacement was refused before it was ever sent. */
  protected readonly replacementRefusal = signal<string | null>(null);

  /**
   * What the newest index attempt did, from the last read.
   *
   * Null means never attempted. Undefined means this page does not know: a
   * document handed over by the create page, or just indexed here.
   */
  protected readonly attempt = signal<KnowledgeIndexAttempt | null | undefined>(undefined);

  /** An index request failed while the reader was here (not a refusal before sending). */
  protected readonly indexFailedNow = signal(false);

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

  /** Whether leaving now would lose edits; asked by the route guard. */
  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

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

  /** Where this text came from, or null when somebody typed it. */
  protected readonly source = computed(() => this.document()?.source ?? null);

  /** The source file size, in the reader's words. */
  protected readonly sourceSize = computed(() => {
    const source = this.source();

    return source ? fileSize(source.byteLength, this.format.language) : '';
  });

  /** The state the page shows: the API's and its last attempt, or a failure just now. */
  protected readonly displayState = computed<string | null>(() => {
    const doc = this.document();

    if (!doc) {
      return null;
    }

    return this.indexFailedNow() && doc.readiness === 'NeedsIndex'
      ? 'Failed'
      : knowledgeDisplayState(doc.readiness, this.attempt());
  });

  /** Why the last attempt failed. A failure just now is said by its own alert. */
  protected readonly failureReason = computed(() => {
    const attempt = this.attempt();

    return attempt?.status === 'Failed' && !this.indexFailedNow()
      ? indexFailureReason(attempt.failureCode)
      : null;
  });

  /** How much text the saved document holds. */
  protected readonly savedSize = computed(() => {
    const length = this.document()?.content.length ?? 0;

    return characterCount(this.format.language, length, this.format.number(length));
  });

  /** The replacement's claimed format and size, for its card. */
  protected readonly replacementFacts = computed(() => {
    const file = this.replacement();

    if (!file) {
      return null;
    }

    const type = claimedFileType(file.name);

    return {
      type: type ? knowledgeTypeWord(type) : null,
      size: fileSize(file.size, this.format.language),
    };
  });

  /** A failed replacement, in the reader's language. */
  protected readonly replaceFailureMessage = computed(() => {
    const error = this.replaceFailure();

    return error ? uploadFailure(error, this.format.language) : null;
  });

  /** Whether pressing Replace would do anything. */
  protected readonly canReplace = computed(
    () =>
      this.replacement() !== null &&
      this.replacementRefusal() === null &&
      !this.replacing() &&
      !this.cannotWrite(),
  );

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
  private readonly handedOver = (() => {
    const state = this.router.currentNavigation()?.extras.state;

    return (state?.[CREATED_DOCUMENT_STATE] ?? state?.[UPLOADED_DOCUMENT_STATE]) as
      Versioned<KnowledgeDocument> | undefined;
  })();

  constructor() {
    // The tab being closed or reloaded is the other way unsaved edits are lost.
    protectUnsavedChanges(() => this.dirty());

    this.form.controls.title.valueChanges.subscribe((value) => this.titleValue.set(value));
    this.form.controls.content.valueChanges.subscribe((value) => this.contentValue.set(value));
  }

  ngOnInit(): void {
    this.listen();

    if (this.handedOver) {
      this.adopt(this.handedOver);
      this.loading.set(false);

      return;
    }

    void this.load();
  }

  /** Hears this document's indexing start and end, for a reader who may view knowledge. */
  private listen(): void {
    const tenantId = this.tenantId();

    if (!tenantId || !this.permissions.has('knowledge.view')) {
      return;
    }

    const stop = this.realtime.listen(tenantId, (documentId) => {
      // Null: the connection is back, and anything may have changed meanwhile.
      if (documentId === null || documentId === this.knowledgeDocumentId()) {
        void this.liveRead();
      }
    });

    this.destroyRef.onDestroy(stop);
  }

  /**
   * Re-reads the document after a knowledge event.
   *
   * Not while this page's own request is out: its answer is the newer word.
   * With unsaved edits, only the document's state is taken (readiness, the newest
   * attempt, when it was indexed); the reader's text and the version it was
   * written against stay, so nothing they typed is replaced.
   */
  private async liveRead(): Promise<void> {
    if (this.liveReading) {
      this.liveAgain = true;

      return;
    }

    if (this.busy()) {
      return;
    }

    this.liveReading = true;

    try {
      do {
        this.liveAgain = false;

        const fresh = await firstValueFrom(
          this.api.get(this.tenantId(), this.knowledgeDocumentId()),
        );
        const current = this.document();

        if (this.busy()) {
          return;
        }

        if (this.dirty() && current) {
          this.document.set({ ...fresh.value, title: current.title, content: current.content });

          if (fresh.value.lastIndexAttempt !== undefined) {
            this.attempt.set(fresh.value.lastIndexAttempt);
          }
        } else {
          this.adopt(fresh);
        }

        // The newest attempt is the truth now, whatever a request here said earlier.
        this.indexFailedNow.set(false);
      } while (this.liveAgain);
    } catch {
      // Leave what is on screen; the next event or visit reads again.
    } finally {
      this.liveReading = false;
    }
  }

  /** A request of this page's own is out: its answer will say what changed. */
  private busy(): boolean {
    return (
      this.loading() ||
      this.saving() ||
      this.indexing() ||
      this.replacing() ||
      this.changingAvailability()
    );
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
          ? $localize`:@@knowledge.toast.savedReindex:Saved. The content changed, so this document needs an index again.`
          : $localize`:@@knowledge.toast.saved:Saved.`,
      );
      this.indexFailedNow.set(false);
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
        message: $localize`:@@knowledge.index.saveFirst:Save your changes first: indexing works from the saved version.`,
      });

      return;
    }

    this.indexing.set(true);
    this.indexFailure.set(null);
    this.indexFailedNow.set(false);
    this.conflict.set(null);
    this.lastIndex.set(null);

    try {
      const result = await firstValueFrom(
        this.api.index(this.tenantId(), this.knowledgeDocumentId(), this.etag() as string),
      );

      this.document.set(result.value.document);
      this.etag.set(result.etag);
      this.attempt.set(undefined);
      this.syncFields(result.value.document);

      this.lastIndex.set({
        chunks: result.value.chunkCount,
        calls: result.value.embeddingCalls,
        current: result.value.alreadyCurrent,
      });

      this.toasts.success(
        result.value.alreadyCurrent
          ? $localize`:@@knowledge.toast.alreadyIndexed:Already up to date. Nothing was sent to the AI provider.`
          : $localize`:@@knowledge.toast.indexed:Indexed. Agents that use this document can answer from it.`,
      );
    } catch (error) {
      this.handleFailure(error, 'index');

      if (this.conflict() === null) {
        this.indexFailedNow.set(true);
      }
    } finally {
      this.indexing.set(false);
    }
  }

  // ------------------------------------------------------------- availability

  protected async setAvailability(enabled: boolean): Promise<void> {
    // Switching it off affects customers at once, so it is confirmed first;
    // switching it back on is not.
    if (!enabled && !(await this.disableDialog().ask())) {
      return;
    }

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
          ? $localize`:@@knowledge.toast.enabled:Back in use. Agents it is selected for can answer from it again.`
          : $localize`:@@knowledge.toast.disabled:Taken out of use. No agent answers from it until you put it back.`,
      );
    } catch (error) {
      this.handleFailure(error, 'availability');
    } finally {
      this.changingAvailability.set(false);
    }
  }

  // ------------------------------------------------------------------- source

  /**
   * Takes the replacement file, and says straight away if it is hopeless.
   *
   * The same courtesy checks the upload page makes, with the same standing: the
   * backend decides what a file really is by reading what is inside it.
   */
  protected chooseReplacement(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;

    this.replaceFailure.set(null);
    this.replacementRefusal.set(null);
    this.replacement.set(file);

    if (file) {
      this.replacementRefusal.set(fileRefusal(file, this.format.language));
    }
  }

  /** Opens the picker for a replacement file: a real button, so keyboards reach it. */
  protected chooseReplacementFile(): void {
    this.replaceInput()?.nativeElement.click();
  }

  /** Forgets the chosen replacement. */
  protected clearReplacement(): void {
    this.replacement.set(null);
    this.replacementRefusal.set(null);
    this.replaceFailure.set(null);

    const input = this.replaceInput()?.nativeElement;

    if (input) {
      input.value = '';
    }
  }

  /**
   * Replaces the file this document was read from.
   *
   * Unsaved edits are refused rather than silently thrown away: the new text
   * replaces the whole document, and doing that over somebody half-finished
   * typing would destroy work they cannot recover.
   */
  protected async replaceSource(): Promise<void> {
    const file = this.replacement();

    if (!file || !this.canReplace()) {
      return;
    }

    if (this.dirty()) {
      this.replaceFailure.set({
        kind: 'validation',
        status: 0,
        message: $localize`:@@knowledge.replace.saveFirst:Save or discard your changes first: replacing the file replaces the whole document.`,
      });

      return;
    }

    this.replacing.set(true);
    this.replaceFailure.set(null);
    this.conflict.set(null);
    this.lastIndex.set(null);

    try {
      const previous = this.document()?.content;

      this.adopt(
        await firstValueFrom(
          this.api.replaceSource(
            this.tenantId(),
            this.knowledgeDocumentId(),
            file,
            this.etag() as string,
          ),
        ),
      );

      this.clearReplacement();

      this.toasts.success(
        previous !== this.document()?.content
          ? $localize`:@@knowledge.toast.replaced:File replaced. The text changed, so this document needs an index again.`
          : $localize`:@@knowledge.toast.replacedSame:That file gave exactly the same text, so nothing changed.`,
      );
      this.indexFailedNow.set(false);
    } catch (error) {
      this.handleFailure(error, 'source');
    } finally {
      this.replacing.set(false);
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
    this.replaceFailure.set(null);

    await this.load();
  }

  /** Deliberately overridable in tests -- the browser's own confirm cannot be driven from one. */
  protected confirmReplace(): boolean {
    return globalThis.confirm(RELOAD_REPLACES_CHANGES_QUESTION);
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
    } else if (source === 'source') {
      this.replaceFailure.set(error);
    } else {
      this.availabilityFailure.set(error);
    }
  }

  /** Takes a server representation as the new canonical state. */
  private adopt(versioned: Versioned<KnowledgeDocument>): void {
    this.document.set(versioned.value);
    this.etag.set(versioned.etag);

    // Only the reads carry it: a write's answer must not read as "never attempted".
    if (versioned.value.lastIndexAttempt !== undefined) {
      this.attempt.set(versioned.value.lastIndexAttempt);
    }

    this.syncFields(versioned.value);
  }

  private syncFields(document: KnowledgeDocument): void {
    this.form.controls.title.setValue(document.title, { emitEvent: false });
    this.titleValue.set(document.title);

    this.form.controls.content.setValue(document.content, { emitEvent: false });
    this.contentValue.set(document.content);
  }
}
