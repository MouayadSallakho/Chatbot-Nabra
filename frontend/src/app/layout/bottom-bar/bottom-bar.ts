import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import {
  NavigationEnd,
  NavigationStart,
  Router,
  RouterLink,
  RouterLinkActive,
} from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { Icon } from '@app/shared/icon/icon';
import { NavCounts } from '@app/layout/nav-counts';
import { trapTab } from '@app/layout/trap-tab';
import {
  INBOX_PATH,
  NAV_GROUPS,
  NAV_GROUP_LABELS,
  NavGroup,
  NavItem,
  navItemForUrl,
  phoneBarSplit,
  visibleNavItems,
} from '@app/layout/navigation';

/** Counts above this read as "99+": the badge sits on a 20px icon. */
const COUNT_CAP = 99;

interface MoreGroup {
  readonly name: NavGroup;
  readonly label: string;
  readonly items: readonly NavItem[];
}

/**
 * The phone's and tablet's bottom bar (Phase 39.3, calm-glass.md §7): four
 * areas and "More".
 *
 * The four are chosen per permission, in the plan's order (the inbox, the
 * overview, the customers, the knowledge; analytics and reports stand in for any
 * the member may not open). Every other area the member may open is in the
 * "More" sheet, under the side bar's section labels. With four areas or fewer
 * there is no "More" at all.
 *
 * The sheet is a native modal `<dialog>`: the page behind it is inert, focus
 * starts on its first link and cannot leave it, Escape closes it, and focus
 * returns to "More". Choosing an area closes it.
 */
@Component({
  selector: 'app-bottom-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, Icon],
  templateUrl: './bottom-bar.html',
  styleUrl: './bottom-bar.scss',
})
export class BottomBar {
  private readonly permissions = inject(PermissionService);
  private readonly counts = inject(NavCounts);
  private readonly format = inject(LocaleFormat);

  private readonly sheet = viewChild<ElementRef<HTMLDialogElement>>('sheet');
  private readonly moreButton = viewChild<ElementRef<HTMLButtonElement>>('moreButton');

  protected readonly inboxPath = INBOX_PATH;
  protected readonly needsPersonWord = $localize`:@@inbox.view.needsPerson:Needs a person`;

  private readonly split = computed(() =>
    phoneBarSplit(visibleNavItems((...codes) => this.permissions.hasAny(...codes))),
  );

  protected readonly bar = computed(() => this.split().bar);

  /** The rest, under the side bar's section labels; empty sections dropped. */
  protected readonly moreGroups = computed<readonly MoreGroup[]>(() => {
    const more = this.split().more;

    return NAV_GROUPS.map((name) => ({
      name,
      label: NAV_GROUP_LABELS[name],
      items: more.filter((item) => item.group === name),
    })).filter((group) => group.items.length > 0);
  });

  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    { initialValue: null },
  );

  /**
   * Whether the current page belongs to one of the areas in the sheet: "More"
   * is then marked as the current place, so the bar always says where you are.
   */
  protected readonly moreActive = computed(() => {
    this.url();
    const item = navItemForUrl(this.router.url);

    return item !== null && this.split().more.includes(item);
  });

  protected readonly open = signal(false);

  protected readonly inboxCount = computed(() => {
    const count = this.counts.needsPerson();

    if (count === null || count <= 0) {
      return null;
    }

    return count > COUNT_CAP ? `${this.format.number(COUNT_CAP)}+` : this.format.number(count);
  });

  /** The exact count, for the words a screen reader hears beside the pill. */
  protected readonly inboxCountWords = computed(() => {
    const count = this.counts.needsPerson();

    return count === null || count <= 0 ? null : this.format.number(count);
  });

  private readonly showsInbox = computed(() => this.bar().some((item) => item.path === INBOX_PATH));

  constructor() {
    effect((onCleanup) => {
      if (this.showsInbox()) {
        onCleanup(this.counts.show());
      }
    });

    // A navigation (an area chosen, a link elsewhere) closes the sheet.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.close(false));
  }

  protected showMore(): void {
    const dialog = this.sheet()?.nativeElement;

    if (!dialog || dialog.open) {
      return;
    }

    this.open.set(true);
    dialog.showModal();
  }

  /** Closes the sheet; focus goes back to "More" unless a page is taking it. */
  protected close(restoreFocus = true): void {
    const dialog = this.sheet()?.nativeElement;

    if (!dialog?.open) {
      return;
    }

    dialog.close();
    this.open.set(false);

    if (restoreFocus) {
      this.moreButton()?.nativeElement.focus();
    }
  }

  /** Escape: the browser closes the dialog; focus goes back to "More". */
  protected onCancel(event: Event): void {
    event.preventDefault();
    this.close();
  }

  /** Tab stays inside the sheet, even past its last area. */
  protected onSheetKeydown(event: KeyboardEvent): void {
    trapTab(event, this.sheet()?.nativeElement);
  }

  /** A tap on the scrim (the dialog's own box outside the sheet) closes it. */
  protected onDialogClick(event: MouseEvent): void {
    if (event.target === this.sheet()?.nativeElement) {
      this.close();
    }
  }
}
