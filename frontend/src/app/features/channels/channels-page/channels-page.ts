import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { CHANNEL_WORDS } from '@app/shared/channel/channel-badge';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { WebChatAdminApi } from '../website-chat-api';
import { WhatsAppApi } from '../whatsapp-api';

/** One built channel's real state, read from its own list. */
type ChannelState<T> =
  | { readonly kind: 'hidden' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'failed' }
  | { readonly kind: 'ready'; readonly value: T };

interface WebChatFacts {
  readonly links: number;
  readonly published: number;
}

interface WhatsAppFacts {
  readonly numbers: number;
  readonly inService: number;
  readonly withoutToken: number;
}

/**
 * The channels a workspace can be reached on (Phase 36.9).
 *
 * <strong>Each built channel shows its real state</strong>, counted from its own
 * list and read only with its own permission: somebody who may see WhatsApp and
 * not website chat sees one card with figures and one that says so. A list that
 * fails to load says "Not checked", never zero.
 *
 * <strong>Two are not built, and say so.</strong> Instagram and Messenger are
 * listed as not available yet, with no button and no link: a dead control, or a
 * page that looks like an unfinished configuration, would be a promise the
 * product cannot keep.
 */
@Component({
  selector: 'app-channels-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, LocalNumberPipe],
  templateUrl: './channels-page.html',
  styleUrl: './channels-page.scss',
})
export class ChannelsPage implements OnInit {
  private readonly permissions = inject(PermissionService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly webChat = inject(WebChatAdminApi);
  private readonly whatsApp = inject(WhatsAppApi);

  protected readonly words = CHANNEL_WORDS;

  protected readonly canViewWebChat = computed(() => this.permissions.has('website-chat.view'));
  protected readonly canViewWhatsApp = computed(() => this.permissions.has('whatsapp.view'));

  /**
   * Whether this page has anything to show.
   *
   * Either channel permission is enough: somebody who administers WhatsApp and
   * not website chat still needs a way in, and each channel's own screen decides
   * what they may do once there.
   */
  protected readonly canView = computed(() => this.canViewWebChat() || this.canViewWhatsApp());

  protected readonly webChatState = signal<ChannelState<WebChatFacts>>({ kind: 'loading' });
  protected readonly whatsAppState = signal<ChannelState<WhatsAppFacts>>({ kind: 'loading' });

  protected readonly webChatFacts = computed(() => {
    const state = this.webChatState();

    return state.kind === 'ready' ? state.value : null;
  });

  protected readonly whatsAppFacts = computed(() => {
    const state = this.whatsAppState();

    return state.kind === 'ready' ? state.value : null;
  });

  ngOnInit(): void {
    void this.loadWebChat();
    void this.loadWhatsApp();
  }

  protected async loadWebChat(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canViewWebChat()) {
      this.webChatState.set({ kind: 'hidden' });

      return;
    }

    this.webChatState.set({ kind: 'loading' });

    try {
      const links = await firstValueFrom(this.webChat.list(tenantId));

      this.webChatState.set({
        kind: 'ready',
        value: {
          links: links.length,
          published: links.filter((link) => link.status === 'Active').length,
        },
      });
    } catch {
      this.webChatState.set({ kind: 'failed' });
    }
  }

  protected async loadWhatsApp(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canViewWhatsApp()) {
      this.whatsAppState.set({ kind: 'hidden' });

      return;
    }

    this.whatsAppState.set({ kind: 'loading' });

    try {
      const numbers = await firstValueFrom(this.whatsApp.list(tenantId));

      this.whatsAppState.set({
        kind: 'ready',
        value: {
          numbers: numbers.length,
          inService: numbers.filter((number) => number.status === 'Active').length,
          withoutToken: numbers.filter((number) => !number.hasCredential).length,
        },
      });
    } catch {
      this.whatsAppState.set({ kind: 'failed' });
    }
  }
}
