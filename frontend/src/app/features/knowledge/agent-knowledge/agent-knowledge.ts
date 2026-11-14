import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ToastService } from '@app/core/feedback/toast-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { Icon } from '@app/shared/icon/icon';
import { Chip } from '@app/shared/chip/chip';
import { knowledgeStateTone, knowledgeStateWord } from '../knowledge-words';
import { KNOWLEDGE_LIMITS } from '../knowledge-limits';
import { AgentKnowledgeOption, KnowledgeApi, KnowledgeSearchHit } from '../knowledge-api';

/**
 * Which knowledge an agent may draw on, and what it would retrieve.
 *
 * <strong>It writes against the agent's entity tag, not one of its own.</strong>
 * Assignment is part of how the agent behaves and is edited beside its
 * instructions, so the version this sends is the agent's -- and a successful
 * write advances it. The parent editor is told, because its own save controls
 * are checked against the same version and would otherwise be stale the moment
 * somebody ticks a box.
 *
 * <strong>On 412 the ticks stay exactly where the reader put them.</strong>
 * Nothing is retried, nothing is merged, and the local selection is not replaced
 * with server state -- reloading is offered as a deliberate choice.
 *
 * <strong>The retrieval tester runs the same code path a live turn runs.</strong>
 * A tester with its own query would tell somebody what a different system would
 * have found.
 *
 * <strong>Two lists, "Selected" and "Available"</strong> (Phase 36.8), instead
 * of one column of checkboxes: what the agent may use is read at a glance, and
 * each document moves with one labelled button ("Add", "Remove"). After a move
 * focus stays in the list it left, on the next document (or the list's
 * heading when it empties), and the move is announced, so a keyboard or screen
 * reader user keeps their place. Nothing is saved until Save, exactly as before.
 */
@Component({
  selector: 'app-agent-knowledge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Icon, LocalNumberPipe, Chip],
  templateUrl: './agent-knowledge.html',
  styleUrl: './agent-knowledge.scss',
})
export class AgentKnowledgeSection implements OnInit {
  private readonly api = inject(KnowledgeApi);
  private readonly permissions = inject(PermissionService);
  private readonly toasts = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly format = inject(LocaleFormat);

  readonly tenantId = input.required<string>();
  readonly aiAgentId = input.required<string>();

  /**
   * The agent's current version, supplied by the editor that owns it.
   *
   * Null while the agent is loading, or after the editor has dropped a tag it
   * knows to be stale. Either way this section cannot write, which is correct.
   */
  readonly agentETag = input<string | null>(null);

  /** The section's number in the editor's form, when it has one. */
  readonly sectionIndex = input<string | null>(null);

  /** Raised with the agent's new version whenever a save advances it. */
  readonly versionChanged = output<string>();

  protected readonly limits = KNOWLEDGE_LIMITS;

  protected readonly stateWord = knowledgeStateWord;
  protected readonly stateTone = knowledgeStateTone;

  /** The last move, said politely to assistive technology. */
  protected readonly announcement = signal('');

  protected readonly loading = signal(true);
  protected readonly loadFailure = signal<ApiError | null>(null);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly saving = signal(false);
  protected readonly stale = signal(false);

  /** Every document in the workspace, with what the server says about each. */
  protected readonly options = signal<readonly AgentKnowledgeOption[]>([]);

  /** What is ticked right now, which may differ from what is saved. */
  protected readonly selected = signal<ReadonlySet<string>>(new Set());

  /** What was ticked when this last matched the server. */
  private readonly saved = signal<ReadonlySet<string>>(new Set());

  protected readonly searching = signal(false);
  protected readonly searchFailure = signal<ApiError | null>(null);
  protected readonly hits = signal<readonly KnowledgeSearchHit[] | null>(null);

  protected readonly search = new FormGroup({
    question: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(KNOWLEDGE_LIMITS.questionMax)],
    }),
  });

  /** Whether the reader may see any of this at all. */
  protected readonly canView = computed(
    () => this.permissions.has('knowledge.view') && this.permissions.has('ai-agents.view'),
  );

  /**
   * Whether the reader may change the assignment.
   *
   * Both permissions, because it is a change to knowledge and a change to an
   * agent at the same time. Neither implies the other, and the server enforces
   * both regardless.
   */
  protected readonly canEdit = computed(
    () => this.permissions.has('knowledge.manage') && this.permissions.has('ai-agents.edit'),
  );

  protected readonly hasDocuments = computed(() => this.options().length > 0);

  /** Whether the ticks differ from what is saved. */
  protected readonly dirty = computed(() => {
    const current = this.selected();
    const stored = this.saved();

    if (current.size !== stored.size) {
      return true;
    }

    for (const id of current) {
      if (!stored.has(id)) {
        return true;
      }
    }

    return false;
  });

  /** How many assigned documents could actually answer something today. */
  protected readonly readyCount = computed(
    () =>
      this.options().filter(
        (option) => this.selected().has(option.knowledgeDocumentId) && option.readiness === 'Ready',
      ).length,
  );

  protected readonly assignedCount = computed(() => this.selected().size);

  /** The documents this agent may draw on, as currently chosen (not yet saved). */
  protected readonly selectedOptions = computed(() =>
    this.options().filter((option) => this.selected().has(option.knowledgeDocumentId)),
  );

  /** Everything else in the workspace. */
  protected readonly availableOptions = computed(() =>
    this.options().filter((option) => !this.selected().has(option.knowledgeDocumentId)),
  );

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    if (!this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.loadFailure.set(null);
    this.stale.set(false);

    try {
      const knowledge = await firstValueFrom(
        this.api.agentKnowledge(this.tenantId(), this.aiAgentId()),
      );

      this.adopt(knowledge.value.documents);
    } catch (error) {
      this.loadFailure.set(toApiError(error));
      this.options.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  protected toggle(documentId: string, checked: boolean): void {
    const next = new Set(this.selected());

    if (checked) {
      next.add(documentId);
    } else {
      next.delete(documentId);
    }

    this.selected.set(next);
  }

  protected isSelected(documentId: string): boolean {
    return this.selected().has(documentId);
  }

  /**
   * Moves one document between the lists, keeping the reader's place.
   *
   * Focus goes to the document that now sits where this one was in the list
   * it left (or the one before it, or that list's heading when it is empty),
   * so pressing the same key again works through a list.
   */
  protected move(option: AgentKnowledgeOption, add: boolean): void {
    if (!this.canEdit()) {
      return;
    }

    const from = add ? this.availableOptions() : this.selectedOptions();
    const index = from.findIndex((item) => item.knowledgeDocumentId === option.knowledgeDocumentId);

    this.toggle(option.knowledgeDocumentId, add);
    this.announcement.set(
      add
        ? $localize`:@@knowledge.assign.added:Added “${option.title}:title:” to this agent. Not saved yet.`
        : $localize`:@@knowledge.assign.removed:Removed “${option.title}:title:” from this agent. Not saved yet.`,
    );

    const list = add ? 'available' : 'selected';
    const remaining = add ? this.availableOptions() : this.selectedOptions();
    const next = remaining[Math.min(index, remaining.length - 1)];

    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        const target = next
          ? root.querySelector<HTMLElement>(`[data-move="${next.knowledgeDocumentId}"]`)
          : root.querySelector<HTMLElement>(`#knowledge-${list}-heading`);

        target?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Button names that start with the visible word, then say which document. */
  protected addLabel(title: string): string {
    return $localize`:@@knowledge.assign.addLabel:Add “${title}:title:”`;
  }

  protected removeLabel(title: string): string {
    return $localize`:@@knowledge.assign.removeLabel:Remove “${title}:title:”`;
  }

  /** "Rank 1 · 87% match · passage 2", in the reader's language. */
  protected hitFacts(hit: KnowledgeSearchHit): string {
    const percent = this.format.percent(Math.max(0, Math.min(1, hit.similarity)));
    const rank = this.format.number(hit.rank);
    const passage = this.format.number(hit.ordinal + 1);

    return $localize`:@@knowledge.retrieval.facts:Rank ${rank}:rank: · ${percent}:percent: match · passage ${passage}:passage:`;
  }

  protected async save(): Promise<void> {
    const etag = this.agentETag();

    if (!this.dirty() || this.saving() || !this.canEdit() || !etag) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.stale.set(false);

    try {
      const replaced = await firstValueFrom(
        this.api.replaceAgentKnowledge(
          this.tenantId(),
          this.aiAgentId(),
          [...this.selected()],
          etag,
        ),
      );

      this.adopt(replaced.value.documents);

      // The agent's version moved, so the editor holding it has to be told or
      // its own next save would be refused against a tag it no longer owns.
      this.versionChanged.emit(replaced.etag);

      this.toasts.success($localize`:@@knowledge.assign.saved:Knowledge saved for this agent.`);
    } catch (error) {
      const failure = toApiError(error);

      if (failure.kind === 'stale' || failure.kind === 'preconditionRequired') {
        // The ticks stay exactly as the reader left them. Replacing them with
        // server state would silently discard the choice they were making.
        this.stale.set(true);

        return;
      }

      this.saveFailure.set(failure);
    } finally {
      this.saving.set(false);
    }
  }

  /** Puts the ticks back to what the server holds. */
  protected reset(): void {
    this.selected.set(new Set(this.saved()));
    this.saveFailure.set(null);
  }

  protected async runSearch(): Promise<void> {
    // Trimmed before the check, because Angular's `required` accepts a string of
    // spaces: it only rejects the empty one. Without this a reader who typed
    // nothing but whitespace would send a request the backend would refuse with
    // a 400, and be shown a validation error for a question they never asked.
    const question = this.search.controls.question.value.trim();

    if (question.length === 0 || this.search.invalid || this.searching()) {
      this.search.markAllAsTouched();

      return;
    }

    this.searching.set(true);
    this.searchFailure.set(null);
    this.hits.set(null);

    try {
      const result = await firstValueFrom(
        this.api.search(this.tenantId(), this.aiAgentId(), question),
      );

      this.hits.set(result.hits);
    } catch (error) {
      this.searchFailure.set(toApiError(error));
    } finally {
      this.searching.set(false);
    }
  }

  private adopt(documents: readonly AgentKnowledgeOption[]): void {
    const assigned = new Set(
      documents.filter((option) => option.isAssigned).map((option) => option.knowledgeDocumentId),
    );

    this.options.set(documents);
    this.saved.set(assigned);
    this.selected.set(new Set(assigned));
  }
}
