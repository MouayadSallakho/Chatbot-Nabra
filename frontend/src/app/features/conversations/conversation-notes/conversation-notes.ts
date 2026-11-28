import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';

import { InboxState } from '../inbox-state';

interface TeamNote {
  readonly id: number;
  readonly author: string | null;
  readonly createdAt: string;
  readonly text: string;
}

interface NotesPage {
  readonly items: TeamNote[];
  readonly nextBefore: number | null;
}

@Component({
  selector: 'app-conversation-notes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conversation-notes.html',
  styleUrl: './conversation-notes.scss',
})
export class ConversationNotes {
  readonly conversationId = input.required<string>();
  readonly canWrite = input.required<boolean>();
  protected readonly format = inject(LocaleFormat);
  private readonly http = inject(HttpClient);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly lifecycle = inject(DestroyRef);
  protected readonly page = signal<NotesPage | null>(null);
  protected readonly before = signal<number | null>(null);
  protected readonly draft = signal('');
  protected readonly saving = signal(false);
  protected readonly readFailed = signal(false);
  protected readonly writeFailed = signal(false);
  private readonly inbox = inject(InboxState, { optional: true });
  private requestId: string = crypto.randomUUID();
  private endpoint = '';
  private readVersion = 0;
  private reading = false;

  constructor() {
    effect((cleanup) => {
      const tenant = this.workspace.tenantId();
      const id = this.conversationId();
      const url = tenant ? `/api/tenants/${tenant}/conversations/${id}/notes` : '';
      untracked(() => {
        this.endpoint = url;
        this.before.set(null);
        this.page.set(null);
        const saved = this.inbox?.draft(url);
        this.draft.set(saved?.text ?? '');
        this.requestId = saved?.key ?? crypto.randomUUID();
        this.writeFailed.set(false);
        this.saving.set(false);
        if (url) void this.refresh();
      });
      const timer = setInterval(() => {
        if (url && !this.reading && this.before() === null && !document.hidden) void this.refresh();
      }, 5000);
      cleanup(() => {
        clearInterval(timer);
        this.readVersion++;
      });
    });
  }

  protected edit(value: string): void {
    this.draft.set(value);
    this.requestId = this.inbox?.keep(this.endpoint, value)?.key ?? crypto.randomUUID();
    this.writeFailed.set(false);
  }

  protected async refresh(): Promise<void> {
    const version = ++this.readVersion;
    this.reading = true;
    const cursor = this.before();
    try {
      const page = await firstValueFrom(
        this.http.get<NotesPage>(this.endpoint, {
          params: cursor === null ? {} : { before: cursor },
        }),
      );
      if (version !== this.readVersion || this.lifecycle.destroyed) return;
      this.page.set(page);
      this.readFailed.set(false);
    } catch {
      if (version !== this.readVersion || this.lifecycle.destroyed) return;
      this.page.set(null);
      this.readFailed.set(true);
    } finally {
      if (version === this.readVersion) this.reading = false;
    }
  }

  protected navigate(cursor: number | null): void {
    this.before.set(cursor);
    this.page.set(null);
    void this.refresh();
  }

  protected async save(): Promise<void> {
    const text = this.draft().trim();
    if (!this.endpoint || !this.canWrite() || !text || this.saving()) return;
    const url = this.endpoint;
    const requestId = this.requestId;
    this.saving.set(true);
    this.writeFailed.set(false);
    try {
      await firstValueFrom(this.http.post<TeamNote>(url, { requestId, text }));
      if (this.inbox?.draft(url)?.key === requestId) this.inbox.sent(url);
      if (url !== this.endpoint || this.lifecycle.destroyed) return;
      this.edit('');
      this.navigate(null);
    } catch {
      if (url === this.endpoint && !this.lifecycle.destroyed) this.writeFailed.set(true);
    } finally {
      if (url === this.endpoint && !this.lifecycle.destroyed) this.saving.set(false);
    }
  }
}
