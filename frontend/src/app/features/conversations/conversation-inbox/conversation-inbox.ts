import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { MediaQueries, mediaQuerySignal } from '@app/core/media/media';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConversationList } from '../conversation-list/conversation-list';

/**
 * The inbox: the conversation list, and whichever conversation is open.
 *
 * <strong>From 1280px, a workspace of panes.</strong> The list stays on screen
 * while a conversation is open beside it, so somebody working a queue never
 * loses their place in it. The open conversation is the child route, so its
 * address is still `/conversations/:id`, Back still works, and a link to it
 * still opens it.
 *
 * <strong>Below 1280px, two pages.</strong> The same components, one at a
 * time: the list, or the conversation with a way back. The list is kept alive
 * while a conversation is open, out of sight, so coming back finds it on the
 * same page and in the same state rather than loading again.
 *
 * <strong>One page heading.</strong> Whichever pane is the page carries the
 * `h1`: the list's title when nothing is open, the conversation's otherwise.
 */
@Component({
  selector: 'app-conversation-inbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, ConversationList],
  templateUrl: './conversation-inbox.html',
  styleUrl: './conversation-inbox.scss',
  host: { '[class.page-fills-content]': 'fillsContent()' },
})
export class ConversationInbox {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly permissions = inject(PermissionService);

  /** Whether the panes sit side by side. */
  protected readonly wide = mediaQuerySignal(MediaQueries.inbox);

  /** Whether an open conversation can fill the screen on its own (see `MediaQueries.tall`). */
  private readonly tall = mediaQuerySignal(MediaQueries.tall);

  /** The open conversation, from the child route; null on the bare list. */
  protected readonly selectedId = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.openConversation()),
    ),
    { initialValue: this.openConversation() },
  );

  /**
   * Whether this reader may read conversations at all.
   *
   * Without it there is nothing to lay out in panes: the list and the
   * conversation each explain the refusal, one at a time.
   */
  protected readonly canView = computed(() => this.permissions.has('conversations.view'));

  /**
   * Whether the inbox fills the content area and scrolls its own panes: always
   * with panes side by side, and on a narrower screen while a conversation is
   * open and the screen is tall enough for one.
   */
  protected readonly fillsContent = computed(
    () => this.canView() && (this.wide() || (this.selectedId() !== null && this.tall())),
  );

  /** The list's heading is the page's when no conversation is open. */
  protected readonly listIsPage = computed(() => this.selectedId() === null);

  private openConversation(): string | null {
    return this.route.firstChild?.snapshot?.paramMap.get('conversationId') ?? null;
  }
}
