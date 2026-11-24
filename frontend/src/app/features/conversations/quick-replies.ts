import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { toApiError } from '@app/core/api/api-error';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';

interface QuickReply {
  id: string;
  text: string;
}

@Component({
  selector: 'app-quick-replies',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label
      ><span i18n="@@quick.language">Reply language</span>
      <select
        #languageSelect
        [value]="language()"
        (change)="language.set(languageSelect.value)"
        [disabled]="saving()"
      >
        <option value="en" lang="en">English</option>
        <option value="ar" lang="ar">العربية</option>
      </select>
    </label>
    @if (error()) {
      <p role="alert">{{ error() }}</p>
      <button
        class="btn btn-secondary btn-sm"
        type="button"
        (click)="reload()"
        i18n="@@quick.retry"
      >
        Reload replies
      </button>
    }
    @if (rows(); as items) {
      @if (!items.length) {
        <p i18n="@@quick.empty">No saved replies in this language.</p>
      }
      @for (item of items; track item.id) {
        <div class="quick-row">
          <button
            type="button"
            class="btn btn-secondary btn-sm quick-text"
            dir="auto"
            (click)="choose.emit(item.text)"
          >
            {{ item.text }}
          </button>
          @if (canManage()) {
            <button
              type="button"
              class="btn btn-quiet btn-sm"
              [disabled]="saving()"
              (click)="remove(item.id)"
              i18n="@@quick.remove"
            >
              Remove
            </button>
          }
        </div>
      }
    } @else if (!error()) {
      <p role="status" i18n="@@quick.loading">Loading replies…</p>
    }
    @if (canManage()) {
      <button
        type="button"
        class="btn btn-secondary btn-sm"
        [disabled]="saving() || !draft().trim() || draft().trim().length > 500"
        (click)="save()"
        i18n="@@quick.save"
      >
        Save draft as a workspace reply
      </button>
      <p class="text-caption" i18n="@@quick.hint">
        Shared with your team in the selected language. Up to 500 characters.
      </p>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-2);
      max-block-size: 16rem;
      overflow: auto;
    }
    .quick-row,
    label {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }
    .quick-text {
      min-inline-size: 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      text-align: start;
    }
  `,
})
export class QuickReplies {
  private readonly http = inject(HttpClient);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  readonly draft = input.required<string>();
  readonly choose = output<string>();
  protected readonly language = signal<string>(inject(LocaleFormat).language);
  protected readonly canManage = computed(() => this.permissions.has('workspace.manage'));
  protected readonly rows = signal<readonly QuickReply[] | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  private readonly revision = signal(0);
  private readonly url = computed(() => {
    const tenant = this.workspace.tenantId();
    return tenant ? `/api/tenants/${tenant}/conversations/quick-replies/${this.language()}` : null;
  });

  constructor() {
    effect((onCleanup) => {
      const url = this.url();
      this.revision();
      this.rows.set(null);
      this.error.set('');
      if (!url) return;
      const read = this.http.get<QuickReply[]>(url).subscribe({
        next: (items) => this.rows.set(items),
        error: (e) => this.error.set(toApiError(e).message),
      });
      onCleanup(() => read.unsubscribe());
    });
  }

  protected reload(): void {
    this.revision.update((n) => n + 1);
  }
  protected save(): Promise<void> {
    return this.change(null);
  }
  protected remove(id: string): Promise<void> {
    return this.change(id);
  }
  private async change(id: string | null): Promise<void> {
    const url = this.url();
    if (!url || !this.canManage() || this.saving()) return;
    this.saving.set(true);
    try {
      await firstValueFrom(
        id ? this.http.delete(url + '/' + id) : this.http.post(url, { text: this.draft().trim() }),
      );
      if (this.url() === url) this.reload();
    } catch (e) {
      if (this.url() === url) this.error.set(toApiError(e).message);
    } finally {
      this.saving.set(false);
    }
  }
}
